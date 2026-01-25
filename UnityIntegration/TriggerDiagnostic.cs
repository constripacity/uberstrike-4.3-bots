using UnityEngine;

namespace UberStrikeBot
{
    public class TriggerDiagnostic : MonoBehaviour
    {
        void Update()
        {
            if (Time.frameCount % 60 != 0) return; // Once per second

            // 1. Check for overlapping colliders (Triggers)
            Collider[] hits = Physics.OverlapSphere(transform.position, 3.0f); // Increased range
            int count = 0;
            foreach (var hit in hits)
            {
                if (count++ > 5) break; // Limit log spam
                
                // BRUTE FORCE DEBUG: Log everything to see what is visible
                Debug.Log("[TriggerDiag] SCAN: " + hit.gameObject.name + 
                          " | IsTrigger: " + hit.isTrigger + 
                          " | Layer: " + hit.gameObject.layer + 
                          " | Tag: " + hit.tag);
            }
        }

        void OnTriggerEnter(Collider other)
        {
            Debug.Log("[TriggerDiag] OnTriggerEnter FIRED on " + gameObject.name + " with " + other.gameObject.name);
        }

        void OnCollisionEnter(Collision collision)
        {
            Debug.Log("[TriggerDiag] OnCollisionEnter FIRED on " + gameObject.name + " with " + collision.gameObject.name);
        }
    }
}