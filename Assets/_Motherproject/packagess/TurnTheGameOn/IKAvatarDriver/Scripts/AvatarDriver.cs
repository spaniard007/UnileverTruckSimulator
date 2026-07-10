namespace TurnTheGameOn.IKAvatarDriver
{
    using UnityEngine;
    using UnityEngine.UI;

    [System.Serializable, RequireComponent(typeof(Animator))]
    public class AvatarDriver : MonoBehaviour
    {
        public float horizontalInput;   // controls steering and hand positions
        public float verticalInput;     // controls accelerate/brake foot
        public string gearString;       // changes trigger shift
        public bool shift;              //set this bool to true to trigger a shift
        public AvatarDriverSettings avatarSettings;
        public Animator animator; // animator for calling IK functions
        public ConfigurableJoint bodyJoint;
        public Transform steeringWheel;
        public Transform readOnlySteeringWheel;
        public Rigidbody vehicleRigidbody; // for calculating steering wheel shake value
        public Text gearText; // when this text changes a shift will be triggered
        public Transform headLookIKCP, targetLeftHandIK, targetRightHandIK, targetRightFootIK, targetLeftFootIK; // Avatar IK control points
        public Transform leftHandObj, rightHandObj, leftFootObj, rightFootObj;									 // current IK targets
        public Transform lhswt_W, lhswt_NW, lhswt_N, lhswt_NE, lhswt_E, lhswt_SE, lhswt_S, lhswt_SW;             // left hand steering wheel IK targets
        public Transform rhswt_W, rhswt_NW, rhswt_N, rhswt_NE, rhswt_E, rhswt_SE, rhswt_S, rhswt_SW;             // right hand steering wheel IK targets
        public Transform leftFootIdle, rightFootIdle, footClutch, footBrake, footGas, handShift, returnShiftTarget;
        private Transform cachedTransform;
        private Transform wheelCP;
        private AvatarDriver_STSReference stsReference;
        private AvatarDriver_RCCReference rccReference;
        private AvatarDriver_EVPReference evpReference;
        private AvatarDriver_NWHReference nwhReference;
        private CarDriveSystem carDriveSystem;
        private bool shifting;                                                                      //used to determine when the right hand should target a steering wheel target or shift target
        private bool targetingWheel;
        private string lastGearString;
        private float lookObjMoveSpeed;                                                                                 //the speed at which the look target object will move
        private int gearInput, lastGearInput;
        private float tempShake;
        private float targetY;
        private float zAngle;
        private float yVelocity;
        private float rotationLimit;
        private float torsoRotation = 0.0f;
        private float torsoLean = 0.0f;
        private float targetRotInput = 0.0f;
        private float targetTorsoLeanSideway = 0.0f;
        private float targetTorsoRotation = 0.0f;
        private float shiftLerp;
        private float holdShiftTimer;
        private float shiftBackTimer;
        private float currentAngleNormalized = 0.0f;
        private float rotateSpeeds = 1.0f;
        private float lookTargetPosX; //the look target transform position.x value
        private Vector3 lookPosition;
        private Quaternion shiftStartRotation;
        private float prevZAngle;
        private float speedometerMultiplier;
        private float deltaTime;

        #region Register callbacks and variables
        void OnEnable()
        {
            bodyJoint.connectedBody = vehicleRigidbody;
            switch (avatarSettings.avatarInputType)
            {
                case AvatarInputType.Player:
                    if (!carDriveSystem) carDriveSystem = GetComponentInParent<CarDriveSystem>();
                    carDriveSystem.OnUpdateInput += OnGetInput;
                    break;
                case AvatarInputType.STS:
                    if (!stsReference) stsReference = GetComponentInParent<AvatarDriver_STSReference>();
                    stsReference.OnUpdateInput_STS += OnGetInput_STS;
                    break;
                case AvatarInputType.RCC:
                    if (!rccReference) rccReference = GetComponent<AvatarDriver_RCCReference>();
                    rccReference.OnUpdateInput_RCC += OnGetInput_RCC;
                    break;
                case AvatarInputType.EVP:
                    if (!evpReference) evpReference = GetComponent<AvatarDriver_EVPReference>();
                    evpReference.OnUpdateInput_EVP += OnGetInput_EVP;
                    break;
                case AvatarInputType.NWH:
                    if (!nwhReference) nwhReference = GetComponent<AvatarDriver_NWHReference>();
                    nwhReference.OnUpdateInput_NWH += OnGetInput_NWH;
                    break;
                case AvatarInputType.Custom:
                    break;
            }
        }

        void OnDisable()
        {
            switch (avatarSettings.avatarInputType)
            {
                case AvatarInputType.Player:
                    carDriveSystem.OnUpdateInput -= OnGetInput;
                    break;
                case AvatarInputType.STS:
                    stsReference.OnUpdateInput_STS -= OnGetInput_STS;
                    break;
                case AvatarInputType.RCC:
                    rccReference.OnUpdateInput_RCC -= OnGetInput_RCC;
                    break;
                case AvatarInputType.EVP:
                    evpReference.OnUpdateInput_EVP -= OnGetInput_EVP;
                    break;
                case AvatarInputType.NWH:
                    nwhReference.OnUpdateInput_NWH -= OnGetInput_NWH;
                    break;
                case AvatarInputType.Custom:
                    break;
            }
        }
        #endregion

        #region Event callbacks
        void OnGetInput()
        {
            horizontalInput = avatarSettings.mobile ? MobileInputManager.GetAxis(avatarSettings.steeringAxis) : Input.GetAxis(avatarSettings.steeringAxis);
            verticalInput = avatarSettings.mobile ? MobileInputManager.GetAxis(avatarSettings.throttleAxis) : Input.GetAxis(avatarSettings.throttleAxis);
        }

        void OnGetInput_STS()
        {
            horizontalInput = Mathf.Clamp(stsReference.sts_Horizontal * avatarSettings.aISteerMultiplier, -1.0f, 1.0f);
            //gearText.text = carDriveSystem.currentGear == 0 ? "N" : carDriveSystem.currentGear == -1 ? "R" : carDriveSystem.currentGear.ToString();
            verticalInput = stsReference.sts_Vertical;
        }

        void OnGetInput_RCC()
        {
            horizontalInput = Mathf.Clamp(rccReference.rcc_Horizontal, -1.0f, 1.0f);
            verticalInput = rccReference.rcc_Vertical;
        }

        void OnGetInput_EVP()
        {
            OnGetInput();
            gearInput = evpReference.evp_Gear;
        }

        void OnGetInput_NWH()
        {
            verticalInput = nwhReference.nwh_Vertical;
            horizontalInput = nwhReference.nwh_Horizontal;
            gearString = nwhReference.nwh_gear;
        }
        #endregion

        void Start()
        {
            cachedTransform = transform;
            transform.localPosition = avatarSettings.avatarPosition;
            lookTargetPosX = avatarSettings.defaultLookXPos;
            TargetShifter();
            rotationLimit = avatarSettings.steeringTargets == SteeringTargets.Two ? avatarSettings.steeringWheelRotationTwoTargets : avatarSettings.steeringWheelRotation;
            wheelCP = avatarSettings.shiftHand == TargetSide.Left ? targetLeftHandIK : avatarSettings.shiftHand == TargetSide.Right ? targetRightHandIK : null;
        }

        void OnAnimatorIK()
        {
            deltaTime = Time.deltaTime;
            if (animator && avatarSettings.ikActive && rightHandObj != null)
            {
                SetShiftState();
                SetIKValues(AvatarIKGoal.LeftHand, 1, 1, targetLeftHandIK.position, targetLeftHandIK.rotation);
                SetIKValues(AvatarIKGoal.RightHand, 1, 1, targetRightHandIK.position, targetRightHandIK.rotation);
                SetIKValues(AvatarIKGoal.LeftFoot, 1, 1, targetLeftFootIK.position, targetLeftFootIK.rotation);
                SetIKValues(AvatarIKGoal.RightFoot, 1, 1, targetRightFootIK.position, targetRightFootIK.rotation);
                SetSteeringWheelRotation();
                SetTorsoRotation();
                UpdateHandTransforms();
                UpdateFootTransforms();
                UpdateIKTargetTransforms();
                UpdateIKLook();
            }
            else
            {
                ZeroIKWeight(AvatarIKGoal.LeftHand);
                ZeroIKWeight(AvatarIKGoal.RightHand);
                ZeroIKWeight(AvatarIKGoal.LeftFoot);
                ZeroIKWeight(AvatarIKGoal.RightFoot);
                animator.SetLookAtWeight(0);
            }
        }

        void SetShiftState()
        {
            if (!avatarSettings.enableShifting)
            {
                return;
            }
            if (avatarSettings.avatarInputType == AvatarInputType.EVP)
            {
                if (gearInput != lastGearInput)
                {
                    lastGearInput = gearInput;
                    TargetShifter();
                }
            }
            else if (avatarSettings.avatarInputType == AvatarInputType.STS)
            {
                if (gearInput != lastGearInput)
                {
                    lastGearInput = gearInput;
                    TargetShifter();
                }
            }
            else if (avatarSettings.avatarInputType == AvatarInputType.NWH)
            {
                if (gearString != lastGearString)
                {
                    lastGearString = gearString;
                    TargetShifter();
                }
            }
            else if (gearText.text != gearString)
            {
                gearString = gearText.text;
                TargetShifter();
            }
            if (shift)
            {
                shift = false;
                TargetShifter();
            }
        }

        private static float WrapAngle(float angle)
        {
            angle %= 360;
            if (angle > 180)
                return angle - 360;

            return angle;
        }

        void SetSteeringWheelRotation()
        {
            if (avatarSettings.controlSteeringWheel)
            {
                if (steeringWheel != null)
                {
                    rotationLimit = avatarSettings.steeringTargets == SteeringTargets.Two ? avatarSettings.steeringWheelRotationTwoTargets : avatarSettings.steeringWheelRotation;
                    tempShake = Random.Range(1.0f, 2.0f);
                    speedometerMultiplier = avatarSettings.speedType == SpeedType.MPH ? 2.23693629f : avatarSettings.speedType == SpeedType.KPH ? 3.6f : 0f;
                    tempShake = tempShake * avatarSettings.wheelShake * 50 * LinearDistance(0, 150, vehicleRigidbody.linearVelocity.magnitude * speedometerMultiplier);
                    tempShake = Random.Range(-tempShake, tempShake);
                    if (horizontalInput == 0) targetY = Mathf.SmoothDamp(targetY, (-(horizontalInput * rotationLimit) - tempShake), ref yVelocity, avatarSettings.snapBackRotationSpeed);
                    else targetY = Mathf.SmoothDamp(targetY, (-(horizontalInput * rotationLimit) - tempShake), ref yVelocity, avatarSettings.rotationSpeed);
                    if (avatarSettings.avatarInputType == AvatarInputType.STS) targetY = Mathf.Clamp(targetY, -rotationLimit, rotationLimit);
                    zAngle = Mathf.SmoothDampAngle(steeringWheel.localEulerAngles.z, targetY, ref yVelocity, avatarSettings.steeringRotationSpeed);
                    if (float.IsNaN(zAngle) == false)
                    {
                        steeringWheel.localEulerAngles = new Vector3(avatarSettings.defaultSteeringWheelRotation.x, avatarSettings.defaultSteeringWheelRotation.y, zAngle);
                        prevZAngle = zAngle;
                    }
                    else
                    {
                        zAngle = prevZAngle;
                    }
                }
            }
            else
            {
                if (readOnlySteeringWheel != null)
                {
                    targetY = WrapAngle(readOnlySteeringWheel.localEulerAngles.z);
                }
            }
        }

        void SetTorsoRotation()
        {
            if (horizontalInput == 0)
            {
                targetRotInput = 0;
            }
            else if (targetY > 0) // turning left ++
            {
                currentAngleNormalized = LinearDistance(0, rotationLimit, zAngle);
                if (steeringWheel.localEulerAngles.z > 0)
                {
                    targetRotInput = avatarSettings.torsoCurve.Evaluate(currentAngleNormalized);
                    targetRotInput = Mathf.Clamp(targetRotInput, -1, 1);
                }
                // targeting left ++
                else
                {
                    targetRotInput = -1 * avatarSettings.torsoCurve.Evaluate(currentAngleNormalized);
                    targetRotInput = Mathf.Clamp(targetRotInput, -1, 1);
                }
            }
            else if (targetY < 0)// turning right --
            {
                currentAngleNormalized = LinearDistance(0, rotationLimit, 360 - zAngle);
                if (steeringWheel.localEulerAngles.z < 0)
                {
                    targetRotInput = avatarSettings.torsoCurve.Evaluate(currentAngleNormalized);
                    targetRotInput = Mathf.Clamp(targetRotInput, -1, 1);
                }  // targeting right --
                else
                {
                    targetRotInput = -1 * avatarSettings.torsoCurve.Evaluate(currentAngleNormalized);
                    targetRotInput = Mathf.Clamp(targetRotInput, -1, 1);
                }
            }
            targetRotInput = targetRotInput > 0 ? avatarSettings.torsoCurve.Evaluate(Mathf.Abs(targetRotInput)) : targetRotInput < 0 ? -1 * avatarSettings.torsoCurve.Evaluate(Mathf.Abs(targetRotInput)) : 0;
            targetTorsoLeanSideway = targetRotInput > 0 ? targetRotInput * avatarSettings.maxLeanLeft : targetRotInput * avatarSettings.maxLeanRight * -1;
            targetTorsoRotation = targetRotInput > 0 ? targetRotInput * avatarSettings.maxRotateLeft : targetRotInput * avatarSettings.maxRotateRight * -1;
            torsoLean = Mathf.SmoothStep(torsoLean, targetTorsoLeanSideway, rotateSpeeds);
            torsoRotation = Mathf.SmoothStep(torsoRotation, targetTorsoRotation, rotateSpeeds);
            cachedTransform.localEulerAngles = new Vector3(avatarSettings.defaultTorsoLeanIn, torsoRotation, torsoLean);
        }

        void SetIKValues(AvatarIKGoal goal, float positionWeight, float rotationWeight, Vector3 position, Quaternion rotation)
        {
            animator.SetIKPositionWeight(goal, positionWeight);
            animator.SetIKRotationWeight(goal, rotationWeight);
            animator.SetIKPosition(goal, position);
            animator.SetIKRotation(goal, rotation);
        }

        void ZeroIKWeight(AvatarIKGoal goal)
        {
            animator.SetIKPositionWeight(goal, 0);
            animator.SetIKRotationWeight(goal, 0);
        }

        void UpdateIKLook()
        {
            if (headLookIKCP != null)
            {
                animator.SetLookAtWeight(1);
                animator.SetLookAtPosition(headLookIKCP.position);
            }
        }

        void UpdateHandTransforms()
        {
            if (avatarSettings.steeringTargets == SteeringTargets.Two)
            {
                rightHandObj = rhswt_E;
                leftHandObj = lhswt_W;
            }
            else if (avatarSettings.steeringTargets == SteeringTargets.All)
            {
                if (targetY >= 0.0f && targetY <= 90.0f || targetY >= 360f && targetY <= 450f)
                {
                    leftHandObj = lhswt_W;
                    rightHandObj = rhswt_E;
                }
                else if (targetY >= 90.0f && targetY <= 180.0f || targetY >= 450 && targetY <= 540f)
                {
                    leftHandObj = lhswt_N;
                    rightHandObj = rhswt_SW;
                }
                else if (targetY >= 180.0f && targetY <= 270.0f || targetY >= 540f && targetY <= 630f)
                {
                    leftHandObj = lhswt_E;
                    rightHandObj = rhswt_NW;
                }
                else if (targetY >= 270f && targetY <= 360f || targetY >= 630f && targetY <= 730f)
                {
                    leftHandObj = lhswt_S;
                    rightHandObj = rhswt_N;
                }

                else if (targetY >= -90.0f && targetY <= 0.0f || targetY >= -450.0f && targetY <= -360.0f)
                {
                    leftHandObj = lhswt_W;
                    rightHandObj = rhswt_E;
                }
                else if (targetY >= -180.0f && targetY <= -90.0f || targetY >= -540.0f && targetY <= -450.0f)
                {
                    leftHandObj = lhswt_S;
                    rightHandObj = rhswt_N;
                }
                else if (targetY >= -270.0f && targetY <= -180.0f || targetY >= -630.0f && targetY <= -540.0f)
                {
                    leftHandObj = lhswt_E;
                    rightHandObj = rhswt_W;
                }
                else if (targetY >= -360.0f && targetY <= -270.0f || targetY >= -730.0f && targetY <= -630.0f)
                {
                    leftHandObj = lhswt_N;
                    rightHandObj = rhswt_S;
                }
            }
            lookTargetPosX = horizontalInput > 0 ? avatarSettings.defaultLookXPos + avatarSettings.maxLookRight :
                horizontalInput < 0 ? avatarSettings.defaultLookXPos + avatarSettings.maxLookLeft :
                avatarSettings.defaultLookXPos;
            lookObjMoveSpeed = horizontalInput > 0 || horizontalInput < 0 ? avatarSettings.minLookSpeed :
                Mathf.Approximately(lookPosition.x, lookTargetPosX) ? avatarSettings.minLookSpeed :
                Mathf.Lerp(lookObjMoveSpeed, avatarSettings.maxLookSpeed, deltaTime);
            rotateSpeeds = horizontalInput > 0 || horizontalInput < 0 ? 0.1f :
                Mathf.Lerp(rotateSpeeds, avatarSettings.maxRotateSpeeds, deltaTime);
        }

        void UpdateFootTransforms()
        {
            if (verticalInput > 0)
            {
                if (avatarSettings.gasFoot == TargetSide.Right)
                {
                    rightFootObj = footGas;
                }
                else if (avatarSettings.gasFoot == TargetSide.Left)
                {
                    rightFootObj = rightFootIdle;
                    leftFootObj = footGas;
                }
            }
            else if (verticalInput < 0)
            {
                if (gearString == "R")
                {   //Reversing	//Set Foot Gas					
                    if (avatarSettings.gasFoot == TargetSide.Right)
                    {
                        rightFootObj = footGas;
                    }
                    else if (avatarSettings.gasFoot == TargetSide.Left)
                    {
                        rightFootObj = rightFootIdle;
                        leftFootObj = footGas;
                    }
                }
                else
                {   //Braking	//Set Foot Brake					
                    if (avatarSettings.brakeFoot == TargetSide.Right)
                    {
                        rightFootObj = footBrake;
                    }
                    else if (avatarSettings.brakeFoot == TargetSide.Left)
                    {
                        leftFootObj = footBrake;
                    }
                }
            }
            else
            {
                rightFootObj = rightFootIdle;
            }
        }

        void UpdateIKTargetTransforms()
        {
            targetRightFootIK.localPosition = Vector3.Lerp(targetRightFootIK.localPosition, rightFootObj.localPosition, 8 * deltaTime);
            targetRightFootIK.localRotation = Quaternion.Lerp(targetRightFootIK.localRotation, rightFootObj.localRotation, 8 * deltaTime);
            targetLeftFootIK.localPosition = Vector3.Lerp(targetLeftFootIK.localPosition, leftFootObj.localPosition, 8 * deltaTime);
            targetLeftFootIK.localRotation = Quaternion.Lerp(targetLeftFootIK.localRotation, leftFootObj.localRotation, 8 * deltaTime);
            if (avatarSettings.shiftHand == TargetSide.Right)
            {
                if (shifting)
                {
                    shiftLerp += avatarSettings.shiftAnimSpeed * deltaTime;
                    if (shiftLerp <= 0.4f)
                    {
                        targetRightHandIK.SetPositionAndRotation(
                            Vector3.Lerp(targetRightHandIK.position, returnShiftTarget.position, LinearDistance(0.0f, 0.4f, shiftLerp)),
                            Quaternion.Lerp(shiftStartRotation, returnShiftTarget.rotation, LinearDistance(0.0f, 0.4f, shiftLerp))
                            );
                    }
                    else if (shiftLerp >= 0.85f)
                    {
                        targetRightHandIK = handShift;
                        holdShiftTimer += deltaTime;
                        if (holdShiftTimer >= avatarSettings.holdShiftTime)
                        {
                            shifting = false;
                            shiftBackTimer = 0.0f;
                            shiftLerp = 0.0f;
                        }
                    }
                    else
                    {
                        targetRightHandIK.SetPositionAndRotation(
                            Vector3.Lerp(returnShiftTarget.position, handShift.position, avatarSettings.shiftCurve.Evaluate(shiftLerp)),
                            Quaternion.Lerp(shiftStartRotation, handShift.rotation, avatarSettings.shiftCurve.Evaluate(shiftLerp))
                            );
                    }
                }
                else
                {
                    if (shiftBackTimer <= avatarSettings.shiftBackTime)
                    {
                        targetRightHandIK = wheelCP;
                        shiftLerp += avatarSettings.shiftAnimSpeed * deltaTime;
                        shiftBackTimer += deltaTime;
                        targetRightHandIK.SetPositionAndRotation(Vector3.Lerp(
                            handShift.position, returnShiftTarget.position, LinearDistance(0.0f, avatarSettings.shiftBackTime, shiftBackTimer)),
                            Quaternion.Lerp(handShift.rotation, returnShiftTarget.rotation, LinearDistance(0.0f, avatarSettings.shiftBackTime, shiftBackTimer))
                            );
                    }
                    else
                    {
                        targetingWheel = true;
                        targetRightHandIK.localPosition = Vector3.Lerp(targetRightHandIK.localPosition, rightHandObj.localPosition, avatarSettings.IKSpeed);
                        targetRightHandIK.localRotation = Quaternion.Lerp(targetRightHandIK.localRotation, rightHandObj.localRotation, 1);
                    }
                }
                targetLeftHandIK.localPosition = Vector3.Slerp(targetLeftHandIK.localPosition, leftHandObj.localPosition, avatarSettings.IKSpeed);
                targetLeftHandIK.localRotation = Quaternion.Lerp(targetLeftHandIK.localRotation, leftHandObj.localRotation, 1);
            }
            else if (avatarSettings.shiftHand == TargetSide.Left)
            {
                if (shifting)
                {
                    shiftLerp += avatarSettings.shiftAnimSpeed * deltaTime;
                    if (shiftLerp <= 0.4f)
                    {
                        targetLeftHandIK.SetPositionAndRotation(
                            Vector3.Lerp(targetLeftHandIK.position, returnShiftTarget.position, LinearDistance(0.0f, 0.4f, shiftLerp)),
                            Quaternion.Lerp(shiftStartRotation, returnShiftTarget.rotation, LinearDistance(0.0f, 0.4f, shiftLerp))
                            );
                    }
                    else if (shiftLerp >= 0.85f)
                    {
                        targetLeftHandIK = handShift;
                        holdShiftTimer += deltaTime;
                        if (holdShiftTimer >= avatarSettings.holdShiftTime)
                        {
                            shifting = false;
                            shiftBackTimer = 0.0f;
                            shiftLerp = 0.0f;
                        }
                    }
                    else
                    {
                        targetLeftHandIK.SetPositionAndRotation(
                            Vector3.Lerp(returnShiftTarget.position, handShift.position, avatarSettings.shiftCurve.Evaluate(shiftLerp)),
                            Quaternion.Lerp(shiftStartRotation, handShift.rotation, avatarSettings.shiftCurve.Evaluate(shiftLerp))
                            );
                    }
                }
                else
                {
                    if (shiftBackTimer <= avatarSettings.shiftBackTime)
                    {
                        targetLeftHandIK = wheelCP;
                        shiftLerp += avatarSettings.shiftAnimSpeed * deltaTime;
                        shiftBackTimer += deltaTime;
                        targetLeftHandIK.SetPositionAndRotation(
                            Vector3.Lerp(handShift.position, returnShiftTarget.position, LinearDistance(0.0f, avatarSettings.shiftBackTime, shiftBackTimer)),
                            Quaternion.Lerp(handShift.rotation, returnShiftTarget.rotation, LinearDistance(0.0f, avatarSettings.shiftBackTime, shiftBackTimer))
                            );
                    }
                    else
                    {
                        targetingWheel = true;
                        targetLeftHandIK.localPosition = Vector3.Lerp(targetLeftHandIK.localPosition, leftHandObj.localPosition, avatarSettings.IKSpeed);
                        targetLeftHandIK.localRotation = Quaternion.Lerp(targetLeftHandIK.localRotation, leftHandObj.localRotation, 1);
                    }
                }
                targetRightHandIK.localPosition = Vector3.Slerp(targetRightHandIK.localPosition, rightHandObj.localPosition, avatarSettings.IKSpeed);
                targetRightHandIK.localRotation = Quaternion.Lerp(targetRightHandIK.localRotation, rightHandObj.localRotation, 1);
            }
            lookPosition = headLookIKCP.localPosition;
            lookPosition.x = Mathf.Lerp(lookPosition.x, lookTargetPosX, lookObjMoveSpeed * deltaTime);
            headLookIKCP.localPosition = lookPosition;
        }

        public void TargetShifter()
        {
            if (!shifting && targetingWheel)
            {
                targetingWheel = false;
                shifting = true;
                shiftLerp = 0.0f;
                holdShiftTimer = 0.0f;
                if (avatarSettings.shiftHand == TargetSide.Left)
                {
                    shiftStartRotation = targetLeftHandIK.rotation;
                }
                else if (avatarSettings.shiftHand == TargetSide.Right)
                {
                    shiftStartRotation = targetRightHandIK.rotation;
                }
                if (avatarSettings.clutchFoot == TargetSide.Left)
                {
                    leftFootObj = footClutch;
                }
                else if (avatarSettings.clutchFoot == TargetSide.Right)
                {
                    rightFootObj = footClutch;
                }
                Invoke(nameof(SetClutchFootIdle), 0.5f);
            }
        }

        public void SetClutchFootIdle()
        {
            if (avatarSettings.clutchFoot == TargetSide.Left)
            {
                leftFootObj = leftFootIdle;
            }
            else if (avatarSettings.clutchFoot == TargetSide.Right)
            {
                rightFootObj = rightFootIdle;
            }
        }

        float LinearDistance(float _start, float _end, float _position)
        {
            return Mathf.InverseLerp(_start, _end, _position);
        }

    }
}