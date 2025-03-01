using UnityEngine;
using System.Linq;
using Unity.Netcode;

public class CameraController : MonoBehaviour
{
    public float smoothSpeed = 0.125f;  // Camera smoothing speed
    public Vector3 cameraRotation = new Vector3(30f, 45f, 0f);  // Fixed isometric rotation for camera
    public float zoomFactor = 1.5f;  // Factor to adjust camera distance based on bounding box size
    public float minDistance = 10f;  // Minimum camera distance to prevent getting too close to players

    private void Start()
    {
        // Set the camera to the correct isometric rotation once at the start
        this.transform.rotation = Quaternion.Euler(cameraRotation);
    }

    private void Update()
    {
        FollowPlayers();
    }

    void FollowPlayers()
    {
        // Get all players in the scene
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");

        if (players.Length == 0)
            return;

        // Calculate the center position of all players
        Vector3 averagePosition = Vector3.zero;
        Vector3 minPosition = Vector3.positiveInfinity;
        Vector3 maxPosition = Vector3.negativeInfinity;

        foreach (var player in players)
        {
            averagePosition += player.transform.position;
            minPosition = Vector3.Min(minPosition, player.transform.position);
            maxPosition = Vector3.Max(maxPosition, player.transform.position);
        }

        averagePosition /= players.Length;  // Average position of all players

        // Calculate the bounding box size based on the players' positions
        Vector3 boundingBoxSize = maxPosition - minPosition;

        // Determine the required camera distance based on the bounding box size
        // We base it on the largest dimension (X or Z) and apply a zoom factor to give a margin
        float requiredDistance = Mathf.Max(boundingBoxSize.x, boundingBoxSize.z) * zoomFactor;

        // Ensure that the camera never gets closer than the minimum distance
        requiredDistance = Mathf.Max(requiredDistance, minDistance);

        // Calculate the direction to place the camera at the right distance from the center of players
        Vector3 desiredPosition = averagePosition - this.transform.forward * requiredDistance;

        // Smoothly move the camera to the new position
        Vector3 smoothedPosition = Vector3.Lerp(this.transform.position, desiredPosition, smoothSpeed);
        this.transform.position = smoothedPosition;

        // Make sure the rotation stays fixed to the isometric view
        this.transform.rotation = Quaternion.Euler(cameraRotation);
    }
}
