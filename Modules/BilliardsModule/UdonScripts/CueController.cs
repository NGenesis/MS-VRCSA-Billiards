﻿using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
//
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class CueController : UdonSharpBehaviour
{
    [SerializeField] private BilliardsModule table;

    [SerializeField] private GameObject primary;
    [SerializeField] private GameObject secondary;
    [SerializeField] private GameObject desktop;
    [SerializeField] private GameObject body;
    [SerializeField] private GameObject cuetip;

    [UdonSynced] private int syncedCueSkin;
    private int activeCueSkin;

    private bool holderIsDesktop;
    [UdonSynced] private bool syncedHolderIsDesktop;

    private bool primaryHolding;
    [UdonSynced] private bool primaryLocked;
    [UdonSynced] private Vector3 primaryLockPos;
    [UdonSynced] private Vector3 primaryLockDir;

    private bool secondaryHolding;
    [UdonSynced] private bool secondaryLocked;
    [UdonSynced] private Vector3 secondaryLockPos;

    private Vector3 secondaryOffset;

    private Vector3 origPrimaryPosition;
    private Vector3 origSecondaryPosition;

    private Vector3 lagPrimaryPosition;
    private Vector3 lagSecondaryPosition;

    private CueGrip primaryController;
    private CueGrip secondaryController;

    private float gripSize;
    private float cuetipDistance;

    private int[] authorizedOwners;

    [NonSerialized] public bool TeamBlue;

    public void _Init()
    {
        primaryController = primary.GetComponent<CueGrip>();
        secondaryController = secondary.GetComponent<CueGrip>();

        primaryController._Init(this, false);
        secondaryController._Init(this, true);

        gripSize = 0.03f;
        cuetipDistance = (cuetip.transform.position - primary.transform.position).magnitude;

        origPrimaryPosition = primary.transform.position;
        origSecondaryPosition = secondary.transform.position;

        lagPrimaryPosition = origPrimaryPosition;
        lagSecondaryPosition = origSecondaryPosition;

        resetSecondaryOffset();
    }

    public override void OnDeserialization()
    {
        int owner = Networking.GetOwner(this.gameObject).playerId;

        activeCueSkin = table._CanUseCueSkin(owner, syncedCueSkin) ? syncedCueSkin : 0;

        refreshCueSkin();
    }

    private void refreshCueSkin()
    {
        MeshRenderer renderer = this.transform.Find("body/render").GetComponent<MeshRenderer>();
        renderer.materials[1].SetTexture("_MainTex", table.cueSkins[activeCueSkin]);
    }

    public void _SetAuthorizedOwners(int[] newOwners)
    {
        authorizedOwners = newOwners;
    }

    public void _Enable()
    {
        primaryController._Show();
    }

    public void _Disable()
    {
        primaryController._Hide();
        secondaryController._Hide();
    }

    public void _ResetCuePosition()
    {
        if (Networking.LocalPlayer.IsOwner(gameObject))
        {
            resetPosition();
        }
    }
    public void _RefreshTable()
    {
        Vector3 newpos;
        if (TeamBlue)
        {
            newpos = table.tableModels[table.tableModelLocal].CueBlue.position;
        }
        else
        {
            newpos = table.tableModels[table.tableModelLocal].CueOrange.position;
        }
        primary.transform.localRotation = Quaternion.identity;
        secondary.transform.localRotation = Quaternion.identity;
        desktop.transform.localRotation = Quaternion.identity;
        origPrimaryPosition = newpos;
        primary.transform.position = origPrimaryPosition;
        origSecondaryPosition = primary.transform.TransformPoint(secondaryOffset);
        secondary.transform.position = origSecondaryPosition;
        lagSecondaryPosition = origSecondaryPosition;
        lagPrimaryPosition = origPrimaryPosition;
        desktop.transform.position = origPrimaryPosition;
        body.transform.position = origPrimaryPosition;
    }
    public void UpdateDesktopPosition()
    {
        desktop.transform.position = body.transform.position;
        desktop.transform.rotation = body.transform.rotation;
    }
    private void FixedUpdate()
    {
        if (primaryHolding)
        {
            // must not be shooting, since that takes control of the cue object
            if (!table.desktopManager._IsInUI() || !table.desktopManager._IsShooting())
            {
                if (!primaryLocked || table.noLockingLocal)
                {
                    // base of cue goes to primary
                    body.transform.position = lagPrimaryPosition;

                    // holding in primary hand
                    if (!secondaryHolding)
                    {
                        // nothing in secondary hand. have the second grip track the cue
                        secondary.transform.position = primary.transform.TransformPoint(secondaryOffset);
                        body.transform.LookAt(lagSecondaryPosition);
                    }
                    else if (!secondaryLocked)
                    {
                        // holding secondary hand. have cue track the second grip
                        body.transform.LookAt(lagSecondaryPosition);
                    }
                    else
                    {
                        // locking secondary hand. lock rotation on point
                        body.transform.LookAt(secondaryLockPos);
                    }

                    // copy z rotation of primary
                    float rotation = primary.transform.localEulerAngles.z;
                    Vector3 bodyRotation = body.transform.localEulerAngles;
                    bodyRotation.z = rotation;
                    body.transform.localEulerAngles = bodyRotation;
                }
                else
                {
                    // locking primary hand. fix cue in line and ignore secondary hand
                    Vector3 delta = lagPrimaryPosition - primaryLockPos;
                    float distance = Vector3.Dot(delta, primaryLockDir);
                    body.transform.position = primaryLockPos + primaryLockDir * distance;
                }

                UpdateDesktopPosition();
            }
            else
            {
                body.transform.position = desktop.transform.position;
                body.transform.rotation = desktop.transform.rotation;
            }

            // clamp controllers
            clampControllers();
        }
        else
        {
            // other player has cue
            if (!syncedHolderIsDesktop)
            {
                // other player is in vr, use the grips which update faster
                if (!primaryLocked || table.noLockingLocal)
                {
                    // base of cue goes to primary
                    body.transform.position = lagPrimaryPosition;

                    // holding in primary hand
                    if (!secondaryLocked)
                    {
                        // have cue track the second grip
                        body.transform.LookAt(lagSecondaryPosition);
                    }
                    else
                    {
                        // locking secondary hand. lock rotation on point
                        body.transform.LookAt(secondaryLockPos);
                    }
                }
                else
                {
                    // locking primary hand. fix cue in line and ignore secondary hand
                    Vector3 delta = lagPrimaryPosition - primaryLockPos;
                    float distance = Vector3.Dot(delta, primaryLockDir);
                    body.transform.position = primaryLockPos + primaryLockDir * distance;
                }
            }
            else
            {
                // other player is on desktop, use the slower synced marker
                body.transform.position = desktop.transform.position;
                body.transform.rotation = desktop.transform.rotation;
            }
        }

        // todo: ugly ugly hack from legacy 8ball. intentionally smooth/lag the position a bit
        // we can't remove this because this directly affects physics
        // must occur at the end after we've finished updating the transform's position
        // otherwise vrchat will try to change it because it's a pickup
        lagPrimaryPosition = Vector3.Lerp(lagPrimaryPosition, primary.transform.position, 1 - Mathf.Pow(0.5f, Time.fixedDeltaTime * 24.0f));
        if (!secondaryLocked)
            lagSecondaryPosition = Vector3.Lerp(lagSecondaryPosition, secondary.transform.position, 1 - Mathf.Pow(0.5f, Time.fixedDeltaTime * 24.0f));
    }

    private Vector3 clamp(Vector3 input, float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
    {
        input.x = Mathf.Clamp(input.x, minX, maxX);
        input.y = Mathf.Clamp(input.y, minY, maxY);
        input.z = Mathf.Clamp(input.z, minZ, maxZ);
        return input;
    }

    private void resetSecondaryOffset()
    {
        Vector3 position = primary.transform.InverseTransformPoint(secondary.transform.position);
        secondaryOffset = position.normalized * Mathf.Clamp(position.magnitude, gripSize * 2, cuetipDistance);
    }

    private void takeOwnership()
    {
        Networking.SetOwner(Networking.LocalPlayer, this.gameObject);
        Networking.SetOwner(Networking.LocalPlayer, primary);
        Networking.SetOwner(Networking.LocalPlayer, secondary);
        Networking.SetOwner(Networking.LocalPlayer, desktop);
    }

    private void resetPosition()
    {
        primary.transform.position = origPrimaryPosition;
        primary.transform.localRotation = Quaternion.identity;
        secondary.transform.position = origSecondaryPosition;
        secondary.transform.localRotation = Quaternion.identity;
        desktop.transform.position = origPrimaryPosition;
        desktop.transform.localRotation = Quaternion.identity;
        body.transform.position = origPrimaryPosition;
        body.transform.LookAt(origSecondaryPosition);
    }

    public void _OnPrimaryPickup()
    {
        takeOwnership();

        holderIsDesktop = !Networking.LocalPlayer.IsUserInVR();
        syncedHolderIsDesktop = holderIsDesktop;
        primaryHolding = true;
        primaryLocked = false;
        syncedCueSkin = table.activeCueSkin;
        RequestSerialization();
        OnDeserialization();

        table._OnPickupCue();

        if (!holderIsDesktop) secondaryController._Show();
    }

    public void _OnPrimaryDrop()
    {
        primaryHolding = false;
        syncedHolderIsDesktop = false;
        RequestSerialization();
        OnDeserialization();

        // hide secondary
        if (!holderIsDesktop) secondaryController._Hide();

        // clamp again
        clampControllers();

        // make sure lag position is reset
        lagPrimaryPosition = primary.transform.position;
        lagSecondaryPosition = secondary.transform.position;

        // move cue to primary grip, since it should be bounded
        body.transform.position = primary.transform.position;
        // make sure cue is facing the secondary grip (since it may have flown off)
        body.transform.LookAt(secondary.transform.position);
        // copy z rotation of primary
        float rotation = primary.transform.localEulerAngles.z;
        Vector3 bodyRotation = body.transform.localEulerAngles;
        bodyRotation.z = rotation;
        body.transform.localEulerAngles = bodyRotation;
        // rotate primary grip to face cue, since cue is visual source of truth
        primary.transform.rotation = body.transform.rotation;
        // reset secondary offset
        resetSecondaryOffset();
        // update desktop marker
        UpdateDesktopPosition();

        table._OnDropCue();
    }

    public void _OnPrimaryUseDown()
    {
        if (!holderIsDesktop)
        {
            primaryLocked = true;
            primaryLockPos = body.transform.position;
            primaryLockDir = body.transform.forward.normalized;
            RequestSerialization();

            table._TriggerCueActivate();
        }
    }

    public void _OnPrimaryUseUp()
    {
        if (!holderIsDesktop)
        {
            primaryLocked = false;
            RequestSerialization();

            table._TriggerCueDeactivate();
        }
    }

    public void _OnSecondaryPickup()
    {
        secondaryHolding = true;
        secondaryLocked = false;
        RequestSerialization();
    }

    public void _OnSecondaryDrop()
    {
        secondaryHolding = false;

        resetSecondaryOffset();
    }

    public void _OnSecondaryUseDown()
    {
        secondaryLocked = true;
        secondaryLockPos = secondary.transform.position;

        RequestSerialization();
    }

    public void _OnSecondaryUseUp()
    {
        secondaryLocked = false;

        RequestSerialization();
    }

    private void clampControllers()
    {
        clampTransform(primary.transform);
        clampTransform(secondary.transform);
    }

    private void clampTransform(Transform child)
    {
        child.position = table.transform.TransformPoint(clamp(table.transform.InverseTransformPoint(child.position), -3.25f, 3.25f, 0f, 3f, -2.5f, 2.5f));
    }

    public GameObject _GetDesktopMarker()
    {
        return desktop;
    }

    public GameObject _GetCuetip()
    {
        return cuetip;
    }

    public VRCPlayerApi _GetHolder()
    {
        return ((VRC_Pickup)primary.GetComponent(typeof(VRC_Pickup))).currentPlayer;
    }
}
