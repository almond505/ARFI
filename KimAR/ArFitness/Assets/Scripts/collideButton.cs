using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class collideButton : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        Debug.Log("BALLL");
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.name == "Sphere") {
            collision.gameObject.GetComponent<Renderer>().material.color = Color.blue;
            Debug.Log("Crash ONE");
        }
      
    }
}
