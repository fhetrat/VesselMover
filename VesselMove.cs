using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VesselMover
{
  [KSPAddon(KSPAddon.Startup.Flight, false)]
  public class VesselMove : MonoBehaviour
  {
    #region Declarations

    public static VesselMove instance;

    public enum MoveModes { Normal = 0, Slow = 1, Fine = 2, Ludicrous = 3 }
    MoveModes moveMode = MoveModes.Normal;

    bool moving = false;
    List<Vessel> placingVessels = new List<Vessel>();

    public float moveHeight = 0;
    float hoverAdjust = 0f;

    float[] hoverHeights = new float[] { 35, 15, 5, 3000 };
    float hoverHeight
    {
      get
      {
        return hoverHeights[(int)moveMode];
      }
    }

    float[] moveSpeeds = new float[] { 10, 0.5f, 0.1f, 1500 };
    float moveSpeed
    {
      get
      {
        return moveSpeeds[(int)moveMode];
      }
    }

    float[] moveAccels = new float[] { 10, 1, 0.5f, 750 };
    float moveAccel
    {
      get
      {
        return moveAccels[(int)moveMode];
      }
    }

    float[] rotationSpeeds = new float[] { 50, 20, 10, 50 };
    float rotationSpeed
    {
      get
      {
        return rotationSpeeds[(int)moveMode] * Time.fixedDeltaTime;
      }
    }

    public bool isMovingVessel = false;
    public Vessel movingVessel;
    Quaternion startRotation;
    Quaternion currRotation;
    float currMoveSpeed = 0;
    Vector3 currMoveVelocity;
    VesselBounds vBounds;
    LineRenderer debugLr;
    Vector3 up;
    Vector3 startingUp;
    float maxPlacementSpeed = 1050;
    bool hasRotated = false;
    float timeBoundsUpdated = 0;
    ScreenMessage moveMessage;

    #endregion

    #region KSP Events

    void Awake()
    {
      if (instance)
      {
        Destroy(instance);
      }
      instance = this;
    }

    void Start()
    {
      debugLr = new GameObject().AddComponent<LineRenderer>();
      debugLr.material = new Material(Shader.Find("KSP/Emissive/Diffuse"));
      debugLr.material.SetColor("_EmissiveColor", Color.green);
      debugLr.SetWidth(0.15f, 0.15f);
      debugLr.enabled = false;
    }

    void Update()
    {
      if (moving)
      {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
          ToggleMoveMode();
        }
      }

      // Changed to a GameEvents handler flag.  This test was too sensitive and caused issues with display.
      // a latched setting will prevent misfires.
      if (isMovingVessel)
      {
        debugLr.enabled = VesselMoverToolbar.ShowUI;
      }
    }

    void FixedUpdate()
    {
      if (moving)
      {
        movingVessel.IgnoreGForces(240);
        UpdateMove();

        if (hasRotated && Time.time - timeBoundsUpdated > 0.2f)
        {
          UpdateBounds();
        }
      }
    }

    void LateUpdate()
    {
      if (moving)
      {
        UpdateDebugLines();
      }
    }

    #endregion

    void UpdateBounds()
    {
      hasRotated = false;
      vBounds.UpdateBounds();
      timeBoundsUpdated = Time.time;
    }

    void UpdateMove()
    {
      if (!movingVessel)
      {
        EndMove();
        return;
      }
      movingVessel.IgnoreGForces(240);

      double alt = movingVessel.radarAltitude;
      if (moveHeight < alt) moveHeight = Convert.ToSingle(alt); // Lerp is animating move from 0 to hoverheight, we do not want this going below current altitude

      moveHeight = Mathf.Lerp(moveHeight, vBounds.bottomLength + hoverHeight + hoverAdjust, 10 * Time.fixedDeltaTime);

      movingVessel.ActionGroups.SetGroup(KSPActionGroup.RCS, false);

      up = (movingVessel.transform.position - FlightGlobals.currentMainBody.transform.position).normalized;

      Vector3 forward;
      if (MapView.MapIsEnabled)
      {
        forward = North();
      }
      else
      {
        forward = Vector3.ProjectOnPlane(movingVessel.CoM - FlightCamera.fetch.mainCamera.transform.position, up).normalized;
        if (Vector3.Dot(-up, FlightCamera.fetch.mainCamera.transform.up) > 0)
        {
          forward = Vector3.ProjectOnPlane(FlightCamera.fetch.mainCamera.transform.up, up).normalized;
        }
      }

      Vector3 right = Vector3.Cross(up, forward);

      Vector3 offsetDirection = Vector3.zero;
      bool inputting = false;

      //Altitude Adjustment
      if (GameSettings.THROTTLE_UP.GetKey())
      {
        hoverAdjust += (10f * Time.fixedDeltaTime);
        inputting = true;
      }

      if (GameSettings.THROTTLE_DOWN.GetKey())
      {
        hoverAdjust += (-10f * Time.fixedDeltaTime);
        inputting = true;
      }

      if (GameSettings.PITCH_DOWN.GetKey())
      {
        offsetDirection += (forward * moveSpeed * Time.fixedDeltaTime);
        inputting = true;
      }
      if (GameSettings.PITCH_UP.GetKey())
      {
        offsetDirection += (-forward * moveSpeed * Time.fixedDeltaTime);
        inputting = true;
      }

      if (GameSettings.YAW_RIGHT.GetKey())
      {
        offsetDirection += (right * moveSpeed * Time.fixedDeltaTime);
        inputting = true;
      }
      if (GameSettings.YAW_LEFT.GetKey())
      {
        offsetDirection += (-right * moveSpeed * Time.fixedDeltaTime);
        inputting = true;
      }

      if (GameSettings.TRANSLATE_RIGHT.GetKey())
      {
        startRotation = Quaternion.AngleAxis(-rotationSpeed, movingVessel.ReferenceTransform.forward) * startRotation;
        hasRotated = true;
      }
      else if (GameSettings.TRANSLATE_LEFT.GetKey())
      {
        startRotation = Quaternion.AngleAxis(rotationSpeed, movingVessel.ReferenceTransform.forward) * startRotation;
        hasRotated = true;
      }

      if (GameSettings.TRANSLATE_DOWN.GetKey())
      {
        startRotation = Quaternion.AngleAxis(rotationSpeed, movingVessel.ReferenceTransform.right) * startRotation;
        hasRotated = true;
      }
      else if (GameSettings.TRANSLATE_UP.GetKey())
      {
        startRotation = Quaternion.AngleAxis(-rotationSpeed, movingVessel.ReferenceTransform.right) * startRotation;
        hasRotated = true;
      }

      if (GameSettings.ROLL_LEFT.GetKey())
      {
        startRotation = Quaternion.AngleAxis(rotationSpeed, movingVessel.ReferenceTransform.up) * startRotation;
        hasRotated = true;
      }
      else if (GameSettings.ROLL_RIGHT.GetKey())
      {
        startRotation = Quaternion.AngleAxis(-rotationSpeed, movingVessel.ReferenceTransform.up) * startRotation;
        hasRotated = true;
      }

      //auto level plane
      if (GameSettings.TRANSLATE_FWD.GetKey())
      {
        Quaternion targetRot = Quaternion.LookRotation(-up, forward);
        startRotation = Quaternion.RotateTowards(startRotation, targetRot, rotationSpeed * 2);
        hasRotated = true;
      }
      else if (GameSettings.TRANSLATE_BACK.GetKey())//auto level rocket
      {
        Quaternion targetRot = Quaternion.LookRotation(forward, up);
        startRotation = Quaternion.RotateTowards(startRotation, targetRot, rotationSpeed * 2);
        hasRotated = true;
      }

      if (inputting)
      {
        currMoveSpeed = Mathf.Clamp(Mathf.MoveTowards(currMoveSpeed, moveSpeed, moveAccel * Time.fixedDeltaTime), 0, moveSpeed);
      }
      else
      {
        currMoveSpeed = 0;
      }

      Vector3 offset = offsetDirection.normalized * currMoveSpeed;
      currMoveVelocity = offset / Time.fixedDeltaTime;
      Vector3 vSrfPt = movingVessel.CoM - (moveHeight * up);
      bool srfBelowWater = false;
      RaycastHit ringHit;

      bool surfaceDetected = CapsuleCast(out ringHit);
      Vector3 finalOffset = Vector3.zero;

      if (surfaceDetected)
      {
        if (FlightGlobals.getAltitudeAtPos(ringHit.point) < 0)
        {
          srfBelowWater = true;
        }

        Vector3 rOffset = Vector3.Project(ringHit.point - vSrfPt, up);
        Vector3 mOffset = (vSrfPt + offset) - movingVessel.CoM;
        finalOffset = rOffset + mOffset + (moveHeight * up);
        movingVessel.Translate(finalOffset);
      }

      PQS bodyPQS = movingVessel.mainBody.pqsController;

      Vector3d geoCoords = WorldPositionToGeoCoords(movingVessel.GetWorldPos3D() + (currMoveVelocity * Time.fixedDeltaTime), movingVessel.mainBody);
      double Lat = geoCoords.x;
      double Lng = geoCoords.y;

      Vector3d bodyUpVector = new Vector3d(1, 0, 0);
      bodyUpVector = QuaternionD.AngleAxis(Lat, Vector3d.forward/*around Z axis*/) * bodyUpVector;
      bodyUpVector = QuaternionD.AngleAxis(Lng, Vector3d.down/*around -Y axis*/) * bodyUpVector;

      double srfHeight = bodyPQS.GetSurfaceHeight(bodyUpVector);

      //double alt = srfHeight - bodyPQS.radius;         
      //double rAlt = movingVessel.radarAltitude;
      //double tAlt = TrueAlt(movingVessel);
      //double pAlt = movingVessel.pqsAltitude;            
      //double teralt = movingVessel.mainBody.TerrainAltitude(movingVessel.mainBody.GetLatitude(geoCoords), movingVessel.mainBody.GetLongitude(geoCoords));
      //Debug.Log ("Surface height: "+movingVessel.mainBody.pqsController.GetSurfaceHeight(up));

      if (!surfaceDetected || srfBelowWater)
      {
        Vector3 terrainPos = movingVessel.mainBody.position + (float)srfHeight * up;
        Vector3 waterSrfPoint = FlightGlobals.currentMainBody.position + ((float)FlightGlobals.currentMainBody.Radius * up);

        if (!surfaceDetected)
        {
          movingVessel.SetPosition(terrainPos + (moveHeight * up) + offset);
        }
        else if (srfBelowWater)
        {
          movingVessel.SetPosition(waterSrfPoint + (moveHeight * up) + offset);
        }

        //update vessel situation to splashed down:
        movingVessel.UpdateLandedSplashed();
      }

      //fix surface rotation
      Quaternion srfRotFix = Quaternion.FromToRotation(startingUp, up);
      currRotation = srfRotFix * startRotation;
      movingVessel.SetRotation(currRotation);

      if (Vector3.Angle(startingUp, up) > 5)
      {
        startRotation = currRotation;
        startingUp = up;
      }

      movingVessel.SetWorldVelocity(Vector3d.zero);
      movingVessel.angularVelocity = Vector3.zero;
      movingVessel.angularMomentum = Vector3.zero;
    }

    Vector3d WorldPositionToGeoCoords(Vector3d worldPosition, CelestialBody body)
    {
      if (!body)
      {
        Debug.LogWarning("WorldPositionToGeoCoords body is null");
        return Vector3d.zero;
      }

      double lat = body.GetLatitude(worldPosition);
      double longi = body.GetLongitude(worldPosition);
      double alt = body.GetAltitude(worldPosition);
      return new Vector3d(lat, longi, alt);
    }

    public void StartMove(Vessel v, bool forceReleaseClamps)
    {
      if (!v)
      {
        Debug.Log("[VesselMover] : Vessel mover tried to move a null vessel.");
      }

      if (v.packed)
      {
        return;
      }

      if (!placingVessels.Contains(v) && v.LandedOrSplashed)
      {
        foreach (LaunchClamp clamp in v.FindPartModulesImplementing<LaunchClamp>())
        {
          if (forceReleaseClamps)
          {
            clamp.Release();
          }
          else
          {
            return;
          }
        }

        ShowModeMessage();

        movingVessel = v;
        isMovingVessel = true;

        up = (v.transform.position - v.mainBody.transform.position).normalized;
        startingUp = up;

        vBounds = new VesselBounds(movingVessel);
        moving = true;
        moveHeight = vBounds.bottomLength + 0.5f;

        startRotation = movingVessel.transform.rotation;
        currRotation = startRotation;

        debugLr.enabled = true;
      }
    }

    public void EndMove()
    {
      StartCoroutine(EndMoveRoutine(vBounds));
      isMovingVessel = false;
      debugLr.enabled = false;
    }

    public void DropMove()
    {
      StartCoroutine(DropMoveRoutine(vBounds));
      isMovingVessel = false;
      debugLr.enabled = false;
    }

    IEnumerator EndMoveRoutine(VesselBounds vesselBounds)
    {
      Vessel v = vesselBounds.vessel;
      if (!v) yield break;

      yield return new WaitForFixedUpdate();
      vesselBounds.UpdateBounds();

      yield return new WaitForFixedUpdate();

      while (moveMode != MoveModes.Normal)
      {
        ToggleMoveMode();
      }

      moving = false;
      moveHeight = 0;
      placingVessels.Add(vesselBounds.vessel);
      float bottomLength = vBounds.bottomLength;

      //float heightOffset = GetRadarAltitude(movingVessel) - moveHeight;

      float altitude = GetRaycastAltitude(vesselBounds);

      while (v && !v.LandedOrSplashed)
      {
        v.IgnoreGForces(240);
        movingVessel.IgnoreGForces(240);

        up = (v.transform.position - FlightGlobals.currentMainBody.transform.position).normalized;
        float placeSpeed = Mathf.Clamp(((altitude - bottomLength) * 2), 0.1f, maxPlacementSpeed);
        if (placeSpeed > 3)
        {
          v.SetWorldVelocity(Vector3.zero);
          movingVessel.angularVelocity = Vector3.zero;
          movingVessel.angularMomentum = Vector3.zero;
          v.Translate(placeSpeed * Time.fixedDeltaTime * -up);
        }
        else
        {
          v.SetWorldVelocity(placeSpeed * -up);
        }
        altitude -= placeSpeed * Time.fixedDeltaTime;
        yield return new WaitForFixedUpdate();
      }

      placingVessels.Remove(v);
      hoverAdjust = 0f;
    }

    IEnumerator DropMoveRoutine(VesselBounds vesselBounds)
    {
      Vessel v = vesselBounds.vessel;
      if (!v) yield break;

      moving = false;
      moveHeight = 0;
      hoverAdjust = 0f;
    }

    void UpdateDebugLines()
    {
      int circleRes = 24;

      Vector3[] positions = new Vector3[circleRes + 3];
      for (int i = 0; i < circleRes; i++)
      {
        positions[i] = GetBoundPoint(i, circleRes, 1);
      }
      positions[circleRes] = GetBoundPoint(0, circleRes, 1);
      positions[circleRes + 1] = movingVessel.CoM;
      positions[circleRes + 2] = movingVessel.CoM + (moveHeight * -up);

      debugLr.positionCount = circleRes + 3;
      debugLr.SetPositions(positions);
    }

    Vector3 GetBoundPoint(int index, int totalPoints, float radiusFactor)
    {
      float angleIncrement = 360 / (float)totalPoints;

      float angle = index * angleIncrement;

      Vector3 forward = North();//Vector3.ProjectOnPlane((movingVessel.CoM)-FlightCamera.fetch.mainCamera.transform.position, up).normalized;

      float radius = vBounds.radius;

      Vector3 offsetVector = (radius * radiusFactor * forward);
      offsetVector = Quaternion.AngleAxis(angle, up) * offsetVector;

      Vector3 point = movingVessel.CoM + offsetVector;

      return point;
    }

    bool CapsuleCast(out RaycastHit rayHit)
    {
      //float radius = (Mathf.Max (Mathf.Max(vesselBounds.size.x, vesselBounds.size.y), vesselBounds.size.z)) + (currMoveSpeed*2);
      float radius = vBounds.radius + Mathf.Clamp(currMoveSpeed, 0, 200);

      return Physics.CapsuleCast(movingVessel.CoM + (250 * up), movingVessel.CoM + (249 * up), radius, -up, out rayHit, 2000, 1 << 15);
    }

    float GetRadarAltitude(Vessel vessel)
    {
      //Not needed anymore - RadarAlt is part of vessel now 
      float radarAlt = Mathf.Clamp((float)(vessel.mainBody.GetAltitude(vessel.CoM) - vessel.terrainAltitude), 0, (float)vessel.altitude);
      return radarAlt;
    }

    float GetRaycastAltitude(VesselBounds vesselBounds) //TODO do the raycast from the bottom point of the ship, and include vessels in layer mask, so you can safely place on top of vessel
    {
      RaycastHit hit;

      //test
      if (Physics.Raycast(vesselBounds.vessel.CoM - (vesselBounds.bottomLength * up), -up, out hit, (float)vesselBounds.vessel.altitude, (1 << 15) | (1 << 0)))
      {
        return Vector3.Project(hit.point - vesselBounds.vessel.CoM, up).magnitude;
      }

      /*
			if(Physics.Raycast(vesselBounds.vessel.CoM, -up, out hit, (float)vesselBounds.vessel.altitude, (1<<15)))
			{
				return hit.distance;
			}*/

      else
      {
        //return GetRadarAltitude(vesselBounds.vessel);
        return (float)vesselBounds.vessel.radarAltitude;
      }
    }

    Vector3 GetRaycastPosition(VesselBounds vesselBounds)
    {
      Vector3 ZeroVector = new Vector3(0, 0, 0);
      RaycastHit hit;
      if (Physics.Raycast(vesselBounds.vessel.CoM - (vesselBounds.bottomLength * up), -up, out hit, (float)vesselBounds.vessel.altitude, (1 << 15) | (1 << 0)))
      {
        return Vector3.Project(hit.point - vesselBounds.vessel.CoM, up);
      }
      else
      {
        return ZeroVector;
      }
    }

    void ToggleMoveMode()
    {
      moveMode = (MoveModes)(int)Mathf.Repeat((float)moveMode + 1, 4);
      ShowModeMessage();

      switch (moveMode)
      {
        case MoveModes.Normal:
          debugLr.material.SetColor("_EmissiveColor", Color.green);
          break;
        case MoveModes.Slow:
          debugLr.material.SetColor("_EmissiveColor", XKCDColors.Orange);
          break;
        case MoveModes.Fine:
          debugLr.material.SetColor("_EmissiveColor", XKCDColors.BrightRed);
          break;
        case MoveModes.Ludicrous:
          debugLr.material.SetColor("_EmissiveColor", XKCDColors.PurpleishBlue);
          break;
      }
    }

    void ShowModeMessage()
    {
      if (moveMessage != null)
      {
        ScreenMessages.RemoveMessage(moveMessage);
      }
      moveMessage = ScreenMessages.PostScreenMessage("Mode : " + moveMode.ToString(), 3, ScreenMessageStyle.UPPER_CENTER);
    }

    Vector3 North()
    {
      Vector3 n = movingVessel.mainBody.GetWorldSurfacePosition(movingVessel.latitude + 1, movingVessel.longitude, movingVessel.altitude) - movingVessel.GetWorldPos3D();
      n = Vector3.ProjectOnPlane(n, up);
      return n.normalized;
    }

    public struct VesselBounds
    {
      public Vessel vessel;
      public float bottomLength;
      public float radius;

      private Vector3 localBottomPoint;
      public Vector3 bottomPoint
      {
        get
        {
          return vessel.transform.TransformPoint(localBottomPoint);
        }
      }

      public VesselBounds(Vessel v)
      {
        vessel = v;
        bottomLength = 0;
        radius = 0;
        localBottomPoint = Vector3.zero;
        UpdateBounds();
      }

      public void UpdateBounds()
      {
        Vector3 up = (vessel.CoM - vessel.mainBody.transform.position).normalized;
        Vector3 forward = Vector3.ProjectOnPlane(vessel.CoM - FlightCamera.fetch.mainCamera.transform.position, up).normalized;
        Vector3 right = Vector3.Cross(up, forward);

        float maxSqrDist = 0;
        Part furthestPart = null;

        //bottom check
        Vector3 downPoint = vessel.CoM - (2000 * up);
        Vector3 closestVert = vessel.CoM;
        float closestSqrDist = Mathf.Infinity;

        //radius check
        Vector3 furthestVert = vessel.CoM;
        float furthestSqrDist = -1;

        foreach (Part p in vessel.parts)
        {
          if (p.Modules.Contains("Tailhook")) return;
          if (p.Modules.Contains("Arrestwire")) return;
          if (p.Modules.Contains("Catapult")) return;
          if (p.Modules.Contains("CLLS")) return;
          if (p.Modules.Contains("OLS")) return;

          float sqrDist = Vector3.ProjectOnPlane((p.transform.position - vessel.CoM), up).sqrMagnitude;
          if (sqrDist > maxSqrDist)
          {
            maxSqrDist = sqrDist;
            furthestPart = p;
          }

          //if(Vector3.Dot(up, p.transform.position-vessel.CoM) < 0)
          //{

          foreach (MeshFilter mf in p.GetComponentsInChildren<MeshFilter>())
          {
            Mesh mesh = mf.mesh;
            foreach (Vector3 vert in mesh.vertices)
            {
              //bottom check
              Vector3 worldVertPoint = mf.transform.TransformPoint(vert);
              float bSqrDist = (downPoint - worldVertPoint).sqrMagnitude;
              if (bSqrDist < closestSqrDist)
              {
                closestSqrDist = bSqrDist;
                closestVert = worldVertPoint;
              }

              //radius check
              //float sqrDist = (vessel.CoM-worldVertPoint).sqrMagnitude;
              float hSqrDist = Vector3.ProjectOnPlane(vessel.CoM - worldVertPoint, up).sqrMagnitude;
              if (hSqrDist > furthestSqrDist)
              {
                furthestSqrDist = hSqrDist;
                furthestVert = worldVertPoint;
              }

            }
          }

          //}
        }

        Vector3 radVector = Vector3.ProjectOnPlane(furthestVert - vessel.CoM, up);
        radius = radVector.magnitude;

        bottomLength = Vector3.Project(closestVert - vessel.CoM, up).magnitude;
        localBottomPoint = vessel.transform.InverseTransformPoint(closestVert);
        //Debug.Log ("Vessel bottom length: "+bottomLength);
        /*
				if(furthestPart!=null)
				{
					//Debug.Log ("Furthest Part: "+furthestPart.partInfo.title);

					Vector3 furthestVert = vessel.CoM;
					float furthestSqrDist = -1;

					foreach(var mf in furthestPart.GetComponentsInChildren<MeshFilter>())
					{
						Mesh mesh = mf.mesh;
						foreach(var vert in mesh.vertices)
						{
							Vector3 worldVertPoint = mf.transform.TransformPoint(vert);
							float sqrDist = (vessel.CoM-worldVertPoint).sqrMagnitude;
							if(sqrDist > furthestSqrDist)
							{
								furthestSqrDist = sqrDist;
								furthestVert = worldVertPoint;
							}
						}
					}

					Vector3 radVector = Vector3.ProjectOnPlane(furthestVert-vessel.CoM, up);
					radius = radVector.magnitude;
					//Debug.Log ("Vert test found radius to be "+radius);
				}
				*/
        //radius *= 1.75f;
        //radius += 5;//15;
        radius += Mathf.Clamp(radius, 2, 10);
      }


    }

    public static List<string> partIgnoreModules = new List<string>(9)
        {
            "Tailhook",
            "Arrestwire",
            "Catapult",
            "CLLS",
            "OLS"
        };

    private static bool IsPartModuleIgnored(string ModuleName)
    {
      return true;
    }

  }
}

