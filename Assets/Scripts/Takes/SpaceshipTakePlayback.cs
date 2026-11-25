using UnityEngine;

namespace Soledad
{
    /// <summary>
    /// On the Spaceship camera target: replays a camera take. The take keys this object's Transform (smooth at every
    /// frame) and <see cref="followTransform"/> = 1. While it is on, the spaceship is put in manual control and given
    /// this Transform as its position before each physics step, so its physics (forces from the gamepad, 50 Hz) stays
    /// out and its own manual control (which sets the rigidbody at 50 Hz) moves nothing: the Timeline places the
    /// camera at every rendered frame. Off again (outside the takes), the spaceship flies on its physics.
    /// </summary>
    [OSCQuery.DoNotExpose]
    [DefaultExecutionOrder(-100)]       // before CameraController.FixedUpdate (UpdateMovement)
    [RequireComponent(typeof(Oxipital.SpaceshipMovement))]
    public class SpaceshipTakePlayback : MonoBehaviour
    {
        [Tooltip("Keyed to 1 by the camera takes: the spaceship follows this Transform (animated by the Timeline) instead of its physics.")]
        public bool followTransform;

        Oxipital.SpaceshipMovement _ship;
        Rigidbody _body;
        RigidbodyInterpolation _interpolation;
        bool _following;

        void Awake()
        {
            _ship = GetComponent<Oxipital.SpaceshipMovement>();
            _body = GetComponent<Rigidbody>();
        }

        void FixedUpdate() => Apply();

        void OnDisable() => Release();

        void Apply()
        {
            if (_ship == null) return;
            if (followTransform)
            {
                if (!_following)
                {
                    // Interpolation would put the body's own pose back on the Transform before each Update, over the
                    // Timeline's (visible while the Timeline is paused: nothing writes the Transform again)
                    if (_body != null)
                    {
                        _interpolation = _body.interpolation;
                        _body.interpolation = RigidbodyInterpolation.None;
                    }
                    _following = true;
                }
                _ship.manualControl = true;
                _ship.cameraPosition = transform.position;
                _ship.cameraRotation = transform.localEulerAngles;
            }
            else Release();
        }

        void Release()
        {
            if (!_following) return;
            _following = false;
            if (_ship != null) _ship.manualControl = false;
            if (_body != null) _body.interpolation = _interpolation;
        }
    }
}
