using UnityEngine;
using System.Collections;
using System.Collections.Generic;


public class NewMonoBehaviourScript : MonoBehaviour
{
    [SerializeField] private Rigidbody rb;
    [SerializeField] private float speed = 5;
    Vector3 input;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    void FixedUpdate()
    {
        move();
    }

    // Update is called once per frame
    void Update()
    {
        input = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
    }

    void move()
    {
        rb.MovePosition(transform.position + input * speed  * Time.deltaTime) ;
    }
}
