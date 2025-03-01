using System.Globalization;
using UnityEngine;
using Unity.Netcode;

public class IsometricMovement : NetworkBehaviour
{
    [SerializeField] private Rigidbody rb;
    [SerializeField] private float speed = 5;

    private Vector3 input;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (!IsOwner) return;
        Debug.Log("spawned");
        GetComponent<Renderer>().material.SetFloat("_IsOwned", 1.0f); ;
    }


    void FixedUpdate()
    {
        if (!IsOwner) return;
        Move();
    }

    // Update is called once per frame
    void Update()
    {
        if (!IsOwner) return;
        input = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
    }

    void Move()
    {
        // Get the camera's forward and right directions (world space)
        Vector3 cameraForward = Camera.main.transform.forward;
        Vector3 cameraRight = Camera.main.transform.right;

        // Flatten out the y-axis to avoid any vertical movement in isometric space
        cameraForward.y = 0;
        cameraRight.y = 0;

        // Normalize the directions
        cameraForward.Normalize();
        cameraRight.Normalize();

        // Calculate the desired direction
        Vector3 desiredDirection = cameraRight * input.x + cameraForward * input.z;

        // Apply movement in the calculated direction
        rb.MovePosition(transform.position + desiredDirection * speed * Time.deltaTime);
    }
}
