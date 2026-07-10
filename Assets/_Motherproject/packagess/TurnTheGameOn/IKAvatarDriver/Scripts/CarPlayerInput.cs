namespace TurnTheGameOn.IKAvatarDriver
{
    using UnityEngine;
    using UnityEngine.EventSystems;
    [RequireComponent(typeof(CarDriveSystem))]
    [RequireComponent(typeof(CarNitro))]
    public class CarPlayerInput : MonoBehaviour
    {
        private CarDriveSystem driveSystem;
        private CarNitro carNitro;
        public CarInputSettings playerInputSettings;
        public CarUIManager dashboardCanvas;
        public CarCameraSwitch vehicleCameraSystem;
        public bool useStandardCanvas { get; private set; }
        public EventTrigger.Entry nitroON;
        public EventTrigger.Entry nitroOFF;
        public EventTrigger.Entry shiftUp;
        public EventTrigger.Entry shiftDown;
        private float accelerationInput, footBrakeInput, horizontalInput, emergencyBrakeInput;
        private bool canShift = true;
        bool canCycleCamera;
        bool isInitialized;

        void Start()
        {
            if (!isInitialized) Initialize();
        }

        public void Initialize()
        {
            isInitialized = true;
            carNitro = GetComponent<CarNitro>();
            driveSystem = GetComponent<CarDriveSystem>();
            useStandardCanvas = playerInputSettings.uIType == UIType.Standalone;
            if (playerInputSettings.uIType == UIType.Mobile)
            {
                if (playerInputSettings.mobileCanvas != null) //Spwan UI Mobile Input
                {
                    driveSystem.vehicleUI = Instantiate(playerInputSettings.mobileCanvas);
                    driveSystem.vehicleUI.RegisterDriveSystem(driveSystem);
                    MobileControlRig mobileRig = driveSystem.vehicleUI.GetComponentInChildren<MobileControlRig>();
                    mobileRig.vehicleController = (CarDriveSystem)driveSystem as CarDriveSystem;

                    EventTrigger mobileButton = GameObject.Find("Nitro Button").GetComponent<EventTrigger>(); //Setup Nitro UI Button
                    EventTrigger.Entry entry = nitroON;
                    mobileButton.triggers.Add(entry);
                    entry = nitroOFF;
                    mobileButton.triggers.Add(entry);

                    if (driveSystem.vehicleSettings.manual)
                    {
                        mobileButton = GameObject.Find("Shift Up Button").GetComponent<EventTrigger>(); //Setup Shift Up UI Button
                        entry = shiftUp;
                        mobileButton.triggers.Add(entry);

                        mobileButton = GameObject.Find("Shift Down Button").GetComponent<EventTrigger>(); //Setup Shift Down UI Button
                        entry = shiftDown;
                        mobileButton.triggers.Add(entry);
                    }
                }
            }
            else
            {
                if (useStandardCanvas && playerInputSettings.defaultCanvas != null) //Spawn UI
                {
                    driveSystem.vehicleUI = Instantiate(playerInputSettings.defaultCanvas);
                }
                else
                {
                    driveSystem.vehicleUI = dashboardCanvas;
                    driveSystem.vehicleUI.primaryUIController = true;
                }
                driveSystem.vehicleUI.RegisterDriveSystem(driveSystem);
            }
        }

        void Update()
        {
            if (carNitro)
            {
                if (Input.GetKeyDown(playerInputSettings.inputAxes.nitroKey)) carNitro.NitroOn();
                if (Input.GetKeyUp(playerInputSettings.inputAxes.nitroKey)) carNitro.NitroOff();
            }
            if (driveSystem.vehicleSettings.manual && canShift)
            {
                if (Input.GetAxisRaw(playerInputSettings.inputAxes.shiftUp) == 1)
                {
                    canShift = false;
                    driveSystem.ShiftUp();
                }
                if (Input.GetAxisRaw(playerInputSettings.inputAxes.shiftDown) == 1)
                {
                    canShift = false;
                    driveSystem.ShiftDown();
                }
            }
            else
            {
                if (Input.GetAxisRaw(playerInputSettings.inputAxes.shiftUp) == 0 && Input.GetAxisRaw(playerInputSettings.inputAxes.shiftDown) == 0) canShift = true;
                //if (Input.GetAxisRaw(playerInputSettings.shiftDownInput) == 0) canShift = true;
            }

            if (vehicleCameraSystem.isLookingBack)
            {
                if (Input.GetKeyUp(playerInputSettings.inputAxes.lookBackKey)) vehicleCameraSystem.OnLookBackKeyUp();
            }
            else
            {
                if (Input.GetKeyDown(playerInputSettings.inputAxes.lookBackKey)) vehicleCameraSystem.LookBackCamera();
            }
        }

        void LateUpdate()
        {
            if (canCycleCamera)
            {
                if (Input.GetKeyDown(playerInputSettings.inputAxes.cycleCameraKey))
                {
                    canCycleCamera = false;
                    vehicleCameraSystem.CycleCamera();
                }
            }
            else if (Input.GetKeyUp(playerInputSettings.inputAxes.cycleCameraKey))
            {
                canCycleCamera = true;
            }
        }

        void FixedUpdate()
        {
            if (playerInputSettings.uIType == UIType.Mobile)
            {
                horizontalInput = MobileInputManager.GetAxis(playerInputSettings.inputAxes.steering);
                accelerationInput = MobileInputManager.GetAxis(playerInputSettings.inputAxes.throttle);
                footBrakeInput = playerInputSettings.inputAxes.invertFootBrake ? -1 * MobileInputManager.GetAxis(playerInputSettings.inputAxes.brake) : MobileInputManager.GetAxis(playerInputSettings.inputAxes.brake);
                emergencyBrakeInput = MobileInputManager.GetAxis(playerInputSettings.inputAxes.handBrake);
            }
            else
            {
                horizontalInput = Input.GetAxis(playerInputSettings.inputAxes.steering);
                accelerationInput = Input.GetAxis(playerInputSettings.inputAxes.throttle);
                footBrakeInput = playerInputSettings.inputAxes.invertFootBrake ? -1 * Input.GetAxis(playerInputSettings.inputAxes.brake) : Input.GetAxis(playerInputSettings.inputAxes.brake);
                emergencyBrakeInput = Input.GetAxis(playerInputSettings.inputAxes.handBrake);
            }

            driveSystem.Move(horizontalInput, accelerationInput, footBrakeInput, emergencyBrakeInput);
        }

    }
}