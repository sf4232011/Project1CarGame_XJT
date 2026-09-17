using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BumperCars
{
    // Only suppress obstructing foliage while this camera renders. Restore every
    // renderer before another split-screen camera renders; never change layers,
    // enabled flags, materials, or the vehicle's colliders.
    [ExecuteAlways, RequireComponent(typeof(Camera))]
    public sealed class BumperCarCameraFoliageVisibility : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Renderer[] foliage = System.Array.Empty<Renderer>();
        [SerializeField] private float targetHeight = 4f;
        [SerializeField] private float targetHalfWidth = 5f;

        private readonly List<HiddenRenderer> hidden = new List<HiddenRenderer>();
        private Camera ownCamera;

        private struct HiddenRenderer
        {
            public Renderer Renderer;
            public bool PreviouslyHidden;
        }

        private void OnEnable()
        {
            ownCamera = GetComponent<Camera>();
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            RenderPipelineManager.endCameraRendering += AfterCamera;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            RenderPipelineManager.endCameraRendering -= AfterCamera;
            Restore();
        }

        private void BeforeCamera(ScriptableRenderContext context, Camera camera)
        {
            Restore();
            if (camera != ownCamera || target == null) return;
            Vector3 center = target.position + Vector3.up * targetHeight;
            foreach (Renderer renderer in foliage)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                bool obstructs = false;
                for (int sample = -1; sample <= 1; sample++)
                {
                    Vector3 direction = center + target.right * (sample * targetHalfWidth) - camera.transform.position;
                    if (renderer.bounds.IntersectRay(new Ray(camera.transform.position, direction), out float distance)
                        && distance < direction.magnitude)
                    {
                        obstructs = true;
                        break;
                    }
                }
                if (!obstructs) continue;
                hidden.Add(new HiddenRenderer { Renderer = renderer, PreviouslyHidden = renderer.forceRenderingOff });
                renderer.forceRenderingOff = true;
            }
        }

        private void AfterCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == ownCamera) Restore();
        }

        private void Restore()
        {
            foreach (HiddenRenderer item in hidden)
                if (item.Renderer != null) item.Renderer.forceRenderingOff = item.PreviouslyHidden;
            hidden.Clear();
        }
    }
}
