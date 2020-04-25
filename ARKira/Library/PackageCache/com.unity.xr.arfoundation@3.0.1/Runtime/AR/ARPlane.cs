using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.XR.ARSubsystems;

namespace UnityEngine.XR.ARFoundation
{
    /// <summary>
    /// Represents a plane (i.e., a flat surface) detected by an AR device.
    /// </summary>
    /// <remarks>
    /// Generated by the <see cref="ARPlaneManager"/> when an AR device detects
    /// a plane in the environment.
    /// </remarks>
    [DefaultExecutionOrder(ARUpdateOrder.k_Plane)]
    [DisallowMultipleComponent]
    [HelpURL("https://docs.unity3d.com/Packages/com.unity.xr.arfoundation@3.0/api/UnityEngine.XR.ARFoundation.ARPlane.html")]
    public sealed class ARPlane : ARTrackable<BoundedPlane, ARPlane>
    {
        [SerializeField]
        [Tooltip("The largest value by which a plane's vertex may change before the boundaryChanged event is invoked. Units are in meters.")]
        float m_VertexChangedThreshold = 0.01f;

        /// <summary>
        /// The largest value by which a plane's vertex may change before the mesh is regenerated. Units are in meters.
        /// </summary>
        public float vertexChangedThreshold
        {
            get => m_VertexChangedThreshold;
            set => m_VertexChangedThreshold = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Invoked when any vertex in the plane's boundary changes by more than <see cref="vertexChangedThreshold"/>.
        /// </summary>
        public event Action<ARPlaneBoundaryChangedEventArgs> boundaryChanged;

        /// <summary>
        /// Gets the normal to this plane in world space.
        /// </summary>
        public Vector3 normal => transform.up;

        /// <summary>
        /// The <see cref="ARPlane"/> which has subsumed this plane, or <c>null</c>
        /// if this plane has not been subsumed.
        /// </summary>
        public ARPlane subsumedBy { get; internal set; }

        /// <summary>
        /// The alignment of this plane.
        /// </summary>
        public PlaneAlignment alignment => sessionRelativeData.alignment;

        /// <summary>
        /// The classification of this plane.
        /// </summary>
        public PlaneClassification classification { get { return sessionRelativeData.classification; } }

        /// <summary>
        /// The 2D center point, in plane space
        /// </summary>
        public Vector2 centerInPlaneSpace => sessionRelativeData.center;

        /// <summary>
        /// The 3D center point, in Unity world space.
        /// </summary>
        public Vector3 center => transform.TransformPoint(new Vector3(centerInPlaneSpace.x, 0, centerInPlaneSpace.y));

        /// <summary>
        /// The physical extents (half dimensions) of the plane in meters.
        /// </summary>
        public Vector2 extents => sessionRelativeData.extents;

        /// <summary>
        /// The physical size (dimensions) of the plane in meters.
        /// </summary>
        public Vector2 size => sessionRelativeData.size;

        /// <summary>
        /// Get the infinite plane associated with this <see cref="ARPlane"/>.
        /// </summary>
        public Plane infinitePlane => new Plane(normal, transform.position);

        /// <summary>
        /// Get a native pointer associated with this plane.
        /// </summary>
        /// <remarks>
        /// The data pointed to by this member is implementation defined.
        /// The lifetime of the pointed to object is also
        /// implementation defined, but should be valid at least until the next
        /// <see cref="ARSession"/> update.
        /// </remarks>
        public IntPtr nativePtr => sessionRelativeData.nativePtr;

        /// <summary>
        /// The plane's boundary points, in plane space, that is, relative to this <see cref="ARPlane"/>'s
        /// local position and rotation.
        /// </summary>
        public unsafe NativeArray<Vector2> boundary
        {
            get
            {
                if (!m_Boundary.IsCreated)
                    return default(NativeArray<Vector2>);

                var boundary = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Vector2>(
                    m_Boundary.GetUnsafePtr(),
                    m_Boundary.Length,
                    Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(
                    ref boundary,
                    NativeArrayUnsafeUtility.GetAtomicSafetyHandle(m_Boundary));
#endif

                return boundary;
            }
        }

        internal void UpdateBoundary(XRPlaneSubsystem subsystem)
        {
            // subsystem cannot be null here
            if (subsystem.SubsystemDescriptor.supportsBoundaryVertices)
            {
                subsystem.GetBoundary(trackableId, Allocator.Persistent, ref m_Boundary);
            }
            else
            {
                if (!m_Boundary.IsCreated)
                {
                    m_Boundary = new NativeArray<Vector2>(4, Allocator.Persistent);
                }
                else if (m_Boundary.Length != 4)
                {
                    m_Boundary.Dispose();
                    m_Boundary = new NativeArray<Vector2>(4, Allocator.Persistent);
                }

                var extents = sessionRelativeData.extents;
                m_Boundary[0] = new Vector2(-extents.x, -extents.y);
                m_Boundary[1] = new Vector2(-extents.x,  extents.y);
                m_Boundary[2] = new Vector2( extents.x,  extents.y);
                m_Boundary[3] = new Vector2( extents.x, -extents.y);
            }

            if (boundaryChanged != null)
                CheckForBoundaryChanges();
        }

        void OnValidate()
        {
            vertexChangedThreshold = Mathf.Max(0f, vertexChangedThreshold);
        }

        void OnDestroy()
        {
            if (m_OldBoundary.IsCreated)
                m_OldBoundary.Dispose();
            if (m_Boundary.IsCreated)
                m_Boundary.Dispose();
        }

        void CheckForBoundaryChanges()
        {
            if (m_Boundary.Length != m_OldBoundary.Length)
            {
                CopyBoundaryAndSetChangedFlag();
            }
            else if (vertexChangedThreshold == 0f)
            {
                // Don't need to check each vertex because it will always
                // be "different" if threshold is zero.
                CopyBoundaryAndSetChangedFlag();
            }
            else
            {
                // Counts are the same; check each vertex
                var thresholdSquared = vertexChangedThreshold * vertexChangedThreshold;
                for (int i = 0; i < m_Boundary.Length; ++i)
                {
                    var diffSquared = (m_Boundary[i] - m_OldBoundary[i]).sqrMagnitude;
                    if (diffSquared > thresholdSquared)
                    {
                        CopyBoundaryAndSetChangedFlag();
                        break;
                    }
                }
            }
        }

        void CopyBoundaryAndSetChangedFlag()
        {
            // Copy new boundary
            if (m_OldBoundary.IsCreated)
            {
                // If the lengths are different, then we need
                // to reallocate, but otherwise, we can reuse
                if (m_OldBoundary.Length != m_Boundary.Length)
                {
                    m_OldBoundary.Dispose();
                    m_OldBoundary = new NativeArray<Vector2>(m_Boundary.Length, Allocator.Persistent);
                }
            }
            else
            {
                m_OldBoundary = new NativeArray<Vector2>(m_Boundary.Length, Allocator.Persistent);
            }

            m_OldBoundary.CopyFrom(m_Boundary);
            m_HasBoundaryChanged = true;
        }

        void Update()
        {
            if (m_HasBoundaryChanged && (boundaryChanged != null))
            {
                m_HasBoundaryChanged = false;
                boundaryChanged(new ARPlaneBoundaryChangedEventArgs(this));
            }
        }

        NativeArray<Vector2> m_Boundary;

        NativeArray<Vector2> m_OldBoundary;

        bool m_HasBoundaryChanged;
    }
}
