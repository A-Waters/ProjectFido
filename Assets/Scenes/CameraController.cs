using System;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private Rigidbody player;
    Vector3 location;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        location = getOffset();
    }

    private void FixedUpdate()
    {
        if (player != null)
        {
            // Get the new position based on the player's position
            Vector3 location = getOffset();
            transform.position = location;

            // Make the camera look at the player
            transform.LookAt(player.transform.position);

            // Apply fixed rotation (45 degrees on X, 35.26 degrees on Y)
            transform.rotation = Quaternion.Euler(45f, 35.26f, 0f);
        }
    }

    // Update is called once per frame
    void Update()
    {
        // You can add smooth camera movement or other logic here if necessary
    }

    Vector3 getOffset()
    {
        // Return the desired offset position for the camera
        return player.transform.position + new Vector3(-10, 10, -10);
    }
}
