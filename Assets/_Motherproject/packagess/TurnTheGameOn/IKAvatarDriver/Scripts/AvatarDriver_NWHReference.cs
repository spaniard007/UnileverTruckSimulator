namespace TurnTheGameOn.IKAvatarDriver
{
    using UnityEngine;
    using NWH.VehiclePhysics2;

    public class AvatarDriver_NWHReference : MonoBehaviour
    {
        public VehicleController vehicleControllerReference;

        public float nwh_Horizontal
        {
            get
            {
                return vehicleControllerReference.input.Steering;
            }
        }
        public float nwh_Vertical
        {
            get
            {
                return vehicleControllerReference.input.Throttle;
            }
        }
        public string nwh_gear
        {
            get
            {
                return vehicleControllerReference.powertrain.transmission.Gear.ToString();
            }
        }

        public delegate void UpdateInput_NWH();
        public event UpdateInput_NWH OnUpdateInput_NWH;

        void Update()
        {
            OnUpdateInput_NWH();
        }
    }
}