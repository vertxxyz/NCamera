/* 2018-03-17 | Thomas Ingram */

#define NCAMERA
using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Reflection;
using UnityEditor.AnimatedValues;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Vertx
{
	[InitializeOnLoad]
	public class NCamera : Editor
	{
		private enum NCameraStatus
		{
			none,
			off,
			sixAxis,
			trackball
		}

		#region Preferences

		private static NCameraStatus _cameraStatus;

		private static NCameraStatus cameraStatus
		{
			get { return _cameraStatus; }
			set
			{
				_cameraStatus = value;
				EditorPrefs.SetInt("vertxCameraStatus", (int) _cameraStatus);
			}
		}

		[Flags]
		private enum ZoomToCursor
		{
			DefaultCamera = 1 << 0,
			SixAxis = 1 << 1,
			Trackball = 1 << 2
		}

		private static ZoomToCursor zoomToCursor = ZoomToCursor.SixAxis | ZoomToCursor.Trackball | ZoomToCursor.DefaultCamera;
		private static bool zoomToCursorIgnoresBackfaces = true;
		private static bool alignCameraAutomatically = true;


		private static bool CanZoomToCursor(NCameraStatus status)
		{
			switch (status)
			{
				case NCameraStatus.sixAxis:
					return (zoomToCursor & ZoomToCursor.SixAxis) != 0;
				case NCameraStatus.trackball:
					return (zoomToCursor & ZoomToCursor.Trackball) != 0;
				default:
					return (zoomToCursor & ZoomToCursor.DefaultCamera) != 0;
			}
		}

		[PreferenceItem("NCamera")]
		private static void Preferences()
		{
			using (EditorGUI.ChangeCheckScope changeCheckScope = new EditorGUI.ChangeCheckScope())
			{
				EditorGUILayout.LabelField("All", EditorStyles.helpBox);
				#if UNITY_2017_3_OR_NEWER
				zoomToCursor = (ZoomToCursor) EditorGUILayout.EnumFlagsField("Zoom To Cursor", zoomToCursor);
				#else
				zoomToCursor = (ZoomToCursor) EditorGUILayout.EnumMaskPopup("Zoom To Cursor", zoomToCursor);
				#endif
				if (changeCheckScope.changed)
				{
					EditorPrefs.SetInt("vertxCameraZoomToCursor", (int) zoomToCursor);
				}
			}

			using (EditorGUI.ChangeCheckScope changeCheckScope = new EditorGUI.ChangeCheckScope())
			{
				zoomToCursorIgnoresBackfaces = EditorGUILayout.Toggle("Zoom To Cursor Ignores Backfaces", zoomToCursorIgnoresBackfaces);

				if (changeCheckScope.changed)
				{
					EditorPrefs.SetBool("vertxZoomToCursorIgnoresBackfaces", zoomToCursorIgnoresBackfaces);
				}
			}

			using (EditorGUI.ChangeCheckScope changeCheckScope = new EditorGUI.ChangeCheckScope())
			{
				EditorGUILayout.LabelField("Six Axis", EditorStyles.helpBox);
				alignCameraAutomatically = EditorGUILayout.Toggle("Align Camera Automatically", alignCameraAutomatically);
				if (changeCheckScope.changed)
				{
					EditorPrefs.SetBool("vertxAlignCameraAutomatically", alignCameraAutomatically);
				}
			}

		}

		#endregion

		#region Setup

		static NCamera()
		{
			LoadSettings();
			RefreshListeners();
			lastRepaintTime = EditorApplication.timeSinceStartup;
		}

		private static void LoadSettings()
		{
			if (!EditorPrefs.HasKey("vertxCameraZoomToCursor"))
				zoomToCursor = ZoomToCursor.SixAxis | ZoomToCursor.Trackball | ZoomToCursor.DefaultCamera;
			else
				zoomToCursor = (ZoomToCursor) EditorPrefs.GetInt("vertxCameraZoomToCursor");
			
			if (!EditorPrefs.HasKey("vertxAlignCameraAutomatically"))
				alignCameraAutomatically = true;
			else
				alignCameraAutomatically = EditorPrefs.GetBool("vertxAlignCameraAutomatically");
			
			if (!EditorPrefs.HasKey("vertxZoomToCursorIgnoresBackfaces"))
				zoomToCursorIgnoresBackfaces = true;
			else
				zoomToCursorIgnoresBackfaces = EditorPrefs.GetBool("vertxZoomToCursorIgnoresBackfaces");
			
			cameraStatus = (NCameraStatus) EditorPrefs.GetInt("vertxCameraStatus");
			
			switch (cameraStatus)
			{
				case NCameraStatus.off:
				case NCameraStatus.none:
					material.mainTexture = textureDefault;
					break;
				case NCameraStatus.sixAxis:
					material.mainTexture = textureSixAxis;
					break;
				default:
					material.mainTexture = textureTrackball;
					break;
			}
		}

		private static void RefreshListeners()
		{
			EditorApplication.update -= OnEditorUpdate;
			EditorApplication.update += OnEditorUpdate;

			// Make sure our listeners are removed so we don't add them again out of order
			SceneView.onSceneGUIDelegate -= OnEarlySceneGUI;
			SceneView.onSceneGUIDelegate -= OnLateSceneGUI;

			// Grab all the remaining listeners
			Delegate[] subscribers = new Delegate[0];
			if (SceneView.onSceneGUIDelegate != null)
			{
				subscribers = SceneView.onSceneGUIDelegate.GetInvocationList();
			}

			// Remove all the listeners - this should result in zero listeners
			foreach (Delegate subscriber in subscribers)
			{
				SceneView.onSceneGUIDelegate -= (SceneView.OnSceneFunc) subscriber;
			}

			SceneView.onSceneGUIDelegate += OnEarlySceneGUI;

			// Re-add all the listeners so they're after the OnEarlySceneGUI call.
			foreach (Delegate subscriber in subscribers)
			{
				SceneView.onSceneGUIDelegate += (SceneView.OnSceneFunc) subscriber;
			}

			SceneView.onSceneGUIDelegate += OnLateSceneGUI;
		}

		#endregion

		private static bool doRepaintScene;

		private static void OnEditorUpdate()
		{
			if (!doRepaintScene) return;
			SceneView.lastActiveSceneView.Repaint();
			doRepaintScene = false;
		}


		private const float flyCamSensitivity = 0.5f;

		/// <summary>
		/// Primarily input operations, called early in the OnSceneGUI call
		/// </summary>
		private static void OnEarlySceneGUI(SceneView sceneView)
		{
			#if !NCAMERA
			return;
			#endif

			Event e = Event.current;
			
			if (e.type == EventType.Repaint)
			{
				repaintDeltaTime = Math.Min(0.1, EditorApplication.timeSinceStartup - lastRepaintTime);
				lastRepaintTime = EditorApplication.timeSinceStartup;
			}

			if (cameraStatus == NCameraStatus.off)
			{
				if (e.type == EventType.Repaint)
				{
					/*Rotate cameras back to normal if toggled off*/
					Vector3 up = sceneView.rotation * Vector3.up;
					Vector3 fwd = sceneView.rotation * Vector3.forward;
					//The Y component of camera-right
					float crossYAbs = Mathf.Abs(up.z * fwd.x - up.x * fwd.z); //Vector3.Cross(up, fwd).y
					if (crossYAbs > 0.01f)
					{
						sceneView.rotation = Quaternion.RotateTowards(sceneView.rotation,
							Quaternion.LookRotation(sceneView.rotation * Vector3.forward, Vector3.up), (float)repaintDeltaTime * axisSpeed);
						doRepaintScene = true;
					}
					else if (crossYAbs > 0.001)
					{
						sceneView.rotation = Quaternion.LookRotation(sceneView.rotation * Vector3.forward, Vector3.up);
						cameraStatus = NCameraStatus.none;
					}
				}
			}

			OnMouseAction(sceneView, e); //This used to be wrapped in a if(e.isMouse), but this causes issues with capturing events outside of the window.
			if (e.isKey)
			{
				OnKeyboardAction(e);
			}

			if (cameraStatus == NCameraStatus.none || cameraStatus == NCameraStatus.none)
				return;

			if (axisSelectorsWereHidden)
			{
				AxisSelectorExtensions.ShowOrHideOtherAxisSelectors(true);
				axisSelectorsWereHidden = false;
			}

			if (e.type == EventType.Repaint)
			{
				//Enable Fly-cam support
				if (rightMouseIsDown)
				{
					if (!altDown && (wDown || sDown || dDown || aDown || qDown || eDown))
					{
						Transform cameraTransform = sceneView.camera.transform;
						sceneView.pivot += cameraTransform.forward *
						                   ((BoolToInt(wDown) - BoolToInt(sDown)) * (float) repaintDeltaTime * flyCamSensitivity);
						sceneView.pivot += cameraTransform.right *
						                   ((BoolToInt(dDown) - BoolToInt(aDown)) * (float) repaintDeltaTime * flyCamSensitivity);
						sceneView.pivot +=
							cameraTransform.up * ((BoolToInt(eDown) - BoolToInt(qDown)) * (float) repaintDeltaTime * flyCamSensitivity);
					}
				}

				//If we're in six-axis mode we should rotate back to Y-up regardless of whether the mouse is being interacted with or not.
				if (cameraStatus == NCameraStatus.sixAxis && !sceneViewAnimatedRot(sceneView).isAnimating)
				{
					if (alignCameraAutomatically)
					{
						Vector3 fwd = sceneView.rotation * Vector3.forward;
						Vector3 right = sceneView.rotation * Vector3.right;
						Vector3 sixAxisUpOrtho = sixAxisUp;
						Vector3.OrthoNormalize(ref fwd, ref sixAxisUpOrtho);
						float dotAbs = Mathf.Abs(Vector3.Dot(right, sixAxisUpOrtho));
						if (dotAbs > 0.01f)
						{
							sceneView.rotation = Quaternion.RotateTowards(sceneView.rotation,
								Quaternion.LookRotation(sceneView.rotation * Vector3.forward, sixAxisUpOrtho),
								(float) repaintDeltaTime * axisSpeed);
							doRepaintScene = true;
						}
						else if (dotAbs > 0.001f)
						{
							sceneView.rotation = Quaternion.LookRotation(sceneView.rotation * Vector3.forward, sixAxisUpOrtho);
						}
					}
				}
			}
		}

		private static double lastRepaintTime;
		private static double repaintDeltaTime;

		#region Animated View Transition Conflicts

		private static AnimQuaternion sceneViewAnimatedRot(SceneView sV)
		{
			return (AnimQuaternion) fI_m_Rotation.GetValue(sV);
		}

		[SerializeField] private static FieldInfo _fI_m_Rotation;

		private static FieldInfo fI_m_Rotation
		{
			get
			{
				return _fI_m_Rotation ?? (_fI_m_Rotation =
					       typeof(SceneView).GetField("m_Rotation", BindingFlags.Instance | BindingFlags.NonPublic));
			}
		}

		#endregion

		/// <summary>
		/// Primarily final drawing, called late in the OnSceneGUI call
		/// </summary>
		private static void OnLateSceneGUI(SceneView sceneView)
		{
			#if !NCAMERA
			return;
						#endif

			if (SceneView.lastActiveSceneView != sceneView) return;

			Event e = Event.current;

			if (e.type == EventType.Repaint)
			{
				OnRepaint(sceneView);
			}

			#region Axis Selectors

			if (cameraStatus == NCameraStatus.none || cameraStatus == NCameraStatus.off) return;

			if (!sceneView.in2DMode)
			{
				if (e.control && !e.alt)
				{
					Action<Quaternion> Callback = q =>
					{
						Transform camTransform = sceneView.camera.transform;
						sixAxisUp = q * Vector3.back;
						sceneView.LookAt(sceneView.pivot, Quaternion.LookRotation(camTransform.forward, sixAxisUp));
					};

					AxisSelectorExtensions.AxisSelectorGUI(sceneView, sixAxisUp, Callback);
				}
			}

			if (!sceneView.in2DMode)
			{
				if (e.control && !e.alt)
				{
					AxisSelectorExtensions.ShowOrHideOtherAxisSelectors(false);
					axisSelectorsWereHidden = true;
				}
			}

			#endregion
		}

		private static bool axisSelectorsWereHidden;

		private static bool leftMouseIsDown;
		private static bool rightMouseIsDown;
		private static bool ctrlDown;
		private static bool altDown;

		private const float sensitivity = 0.75f;
		private const float axisSpeed = 200f;
		private const float trackBallSizeNormalised = 0.8f;
		private const float zoomToCursorPivotIntensity = 0.8f;

		private static Vector3 sixAxisUp = Vector3.up;
		private static float deltaZoomOverall;
		private static float lastCameraDistance;
		private static Vector3 lastCameraPosition;
		
		private static void OnMouseAction(SceneView sceneView, Event e)
		{
			Camera cam = sceneView.camera;

			bool mouseDrag = false;
			bool leftMouseDown = false;
			bool leftMouseUp = false;
			bool rightMouseDown = false;
			bool rightMouseUp = false;
			if (e.button == 0)
			{
				switch (e.rawType)
				{
					case EventType.MouseDown:
						leftMouseIsDown = true;
						leftMouseDown = true;
						break;
					case EventType.MouseUp:
					case EventType.Used:
						//The left mouse up is being eaten, but it isn't if we just assume it's the only Used event (lololo)
						leftMouseUp = true;
						leftMouseIsDown = false;
						break;
					case EventType.MouseDrag:
						if (leftMouseIsDown)
							mouseDrag = true;
						break;
				}
			}
			else if (e.button == 1)
			{
				switch (e.rawType)
				{
					case EventType.MouseDown:
						rightMouseDown = true;
						rightMouseIsDown = true;
						break;
					case EventType.MouseUp:
						rightMouseUp = true;
						rightMouseIsDown = false;
						break;
					case EventType.MouseDrag:
						if (rightMouseIsDown)
							mouseDrag = true;
						break;
				}
			}

			if ((e.control || e.command) && leftMouseDown)
				ctrlDown = true;
			if (e.alt && (leftMouseDown || rightMouseDown))
				altDown = true;

			if (!leftMouseIsDown && !rightMouseIsDown)
			{
				ctrlDown = false;
				altDown = false;
			}

			#region Switch Button

			Vector2 mouseGUIPosition = new Vector2(sceneView.camera.pixelWidth - e.mousePosition.x, e.mousePosition.y);
			if (leftMouseUp)
			{
				//Check Against the switch Button
				if (buttonRect.Contains(mouseGUIPosition))
				{
					if (cameraStatus == NCameraStatus.off || cameraStatus == NCameraStatus.none)
					{
						cameraStatus = NCameraStatus.trackball;
						material.mainTexture = textureTrackball;
						RefreshListeners(); //Just refreshing the listeners whenever the button does a loop just in case another plugin has inserted itself earlier in the stack
					}
					else if (cameraStatus == NCameraStatus.sixAxis)
					{
						cameraStatus = NCameraStatus.off;
						material.mainTexture = textureDefault;
					}
					else
					{
						cameraStatus = NCameraStatus.sixAxis;
						material.mainTexture = textureSixAxis;
					}

					e.Use();
				}
			}
			else if (rightMouseUp)
			{
				//Check Against the switch Button
				if (buttonRect.Contains(mouseGUIPosition))
				{
					GUIContent[] options =
					{
						new GUIContent("Default"),
						new GUIContent(""),
						new GUIContent("Trackball"),
						new GUIContent("Six-Axis")
					};
					int selected;
					switch (cameraStatus)
					{
						case NCameraStatus.sixAxis:
							selected = 3;
							break;
						case NCameraStatus.trackball:
							selected = 2;
							break;
						default:
							selected = 0;
							break;
					}

					EditorUtility.DisplayCustomMenu(GetContextRect(sceneView.position.width), options, selected, ContextMenuDelegate,
						sceneView);
					rightMouseIsDown = false;
					e.Use();
				}
			}

			#endregion

			bool offNone = cameraStatus == NCameraStatus.none || cameraStatus == NCameraStatus.off;

			//Enable Right-Mouse pan
			if (rightMouseIsDown && e.type != EventType.Layout && e.type != EventType.Repaint)
			{
				if (!altDown)
				{
					if (!sceneView.isRotationLocked && !sceneView.in2DMode && !offNone)
					{
						Transform cameraTransform = cam.transform;

						//Rotate pivot around camera position (right-mouse pan)
						if (mouseDrag)
						{
							sceneView.pivot -= cameraTransform.position;
							Quaternion xRot = Quaternion.AngleAxis(e.delta.x * 0.25f * sensitivity,
								cameraStatus == NCameraStatus.sixAxis ? sixAxisUp : cameraTransform.up);
							Quaternion yRot = Quaternion.AngleAxis(e.delta.y * 0.25f * sensitivity, cameraTransform.right);
							sceneView.pivot = xRot * sceneView.pivot;
							sceneView.pivot = yRot * sceneView.pivot;
							sceneView.pivot += cameraTransform.position;


							sceneView.rotation = xRot * sceneView.rotation;
							sceneView.rotation = yRot * sceneView.rotation;
							//Override default Right-Mouse pan behaviour
							e.Use();
						}
					}
				}
				else
				{
					if (CanZoomToCursor(cameraStatus) && !e.control)
					{
						//Functionality for zoom-to-cursor (and pivot manipulation with zoom)
						if (rightMouseDown)
						{
							if (RaycastWorld(e.mousePosition, out hit, zoomToCursorIgnoresBackfaces))
							{
								if (Vector3.Dot(sceneView.camera.transform.forward, hit.point - sceneView.camera.transform.position) < 0)
								{
									didHitOnZoom = false;
								}
								else
								{
									lastPivot = sceneView.pivot;

									deltaZoomOverall = 0;
//									Debug.DrawRay(hit.point, hit.normal * 20, Color.green);
//									Debug.DrawLine(hit.point, Vector3.zero, Color.green);
									lastCameraDistance = sceneView.cameraDistance;
									lastCameraPosition = sceneView.camera.transform.position;
									didHitOnZoom = true;
								}
							}else{
								didHitOnZoom = false;
							}
						}
						else if (didHitOnZoom)
						{
							const float min = 0.01f;

							float deltaX = e.delta.x / sceneView.camera.pixelWidth;
							float deltaY = e.delta.y / sceneView.camera.pixelHeight;
							float deltaMax = Mathf.Max(deltaX, deltaY);
							float deltaMin = Mathf.Min(deltaX, deltaY);
							float delta = MinOrMax(deltaMax, deltaMin);
							deltaZoomOverall += delta;
							float deltaToUse = deltaZoomOverall * 0.5f;
							//If we're currently zooming in then we want to zoom to cursor, zooming out will be handled by the default Unity implementation.
							if (deltaZoomOverall > 0)
							{

								Vector3 destination = hit.point + hit.normal * 0.01f;
								sceneView.pivot = Vector3.Lerp(lastPivot, destination, Mathf.Pow(deltaToUse, zoomToCursorPivotIntensity));

								if (!sceneView.orthographic)
								{
									
									//This section of code removes the zoom offset of the camera from the resultant scene view pivot change.
									//It also removes the difference between the last camera position and the intended one.
									//The resultant change should be a lerp of the camera between its last position and the position under the cursor.
									Vector3 dir = sceneView.camera.transform.forward;
									Vector3 pivotChange = sceneView.pivot - lastPivot;
									
									Vector3 intendedCameraPosition = Vector3.Lerp(lastCameraPosition, destination, deltaToUse);
									Vector3 cameraChange = intendedCameraPosition - lastCameraPosition;
									
									Vector3 overallChange = -cameraChange + pivotChange;
									float diffPivot = Vector3.Project(overallChange, dir).magnitude * Mathf.Sign(Vector3.Dot(dir, overallChange));

									float newCameraDistance = lastCameraDistance + diffPivot;
									//------------------------------------------------------------------------------------------------------
									
									//90 is kPerspectiveFov in SceneView.
									//Convert camera distance to sceneview size.
									sceneView.size = newCameraDistance * Mathf.Tan(90 * 0.5f * Mathf.Deg2Rad);
									sceneView.size = Mathf.Max(min, sceneView.size);
								}

								float farClip = Mathf.Max(1000f, 2000f * sceneView.size);
								cam.nearClipPlane = farClip * 5E-06f;
								cam.farClipPlane = farClip;
								e.Use();
							}
						}
					}
				}
			}

			//If not doing anything else, return
			if (offNone)
				return;

			bool panBehaviour = false;
			if (altDown && !ctrlDown && !sceneView.isRotationLocked && !sceneView.in2DMode)
			{
				if (leftMouseDown)
					sceneView.FixNegativeSize();
				if (leftMouseIsDown && mouseDrag)
					panBehaviour = true;
			}

			if (cameraStatus == NCameraStatus.trackball)
			{
				if (panBehaviour)
				{
					int widthHeightMin = Mathf.Min(cam.pixelWidth, cam.pixelHeight);
					float distX =
						Mathf.Clamp((e.mousePosition.x - cam.pixelWidth / 2f) / (trackBallSizeNormalised * widthHeightMin / 2f), -1, 1);
					float distY =
						Mathf.Clamp((e.mousePosition.y - cam.pixelHeight / 2f) / (trackBallSizeNormalised * widthHeightMin / 2f), -1, 1);
					float valX = Mathf.Clamp01(1 - Mathf.Abs(distX));
					float valY = Mathf.Clamp01(1 - Mathf.Abs(distY));
					sceneView.rotation = sceneView.rotation *
					                     Quaternion.AngleAxis(e.delta.x * 0.25f * -distY * sensitivity, Vector3.forward);
					sceneView.rotation = sceneView.rotation *
					                     Quaternion.AngleAxis(e.delta.y * 0.25f * distX * sensitivity, Vector3.forward);
					sceneView.rotation = sceneView.rotation * Quaternion.AngleAxis(e.delta.x * 0.5f * valY * sensitivity, Vector3.up);
					sceneView.rotation =
						sceneView.rotation * Quaternion.AngleAxis(e.delta.y * 0.5f * valX * sensitivity, Vector3.right);
					e.Use();
				}
			}
			else
			{
				//if(cameraStatus == FreeCameraStatus.sixAxis){
				if (alignCameraAutomatically)
				{
					float dot = Vector3.Dot(sixAxisUp, cam.transform.up);
					float det = sixAxisUp.x * cam.transform.up.y * cam.transform.right.z +
					            cam.transform.up.x * cam.transform.right.y * sixAxisUp.z +
					            cam.transform.right.x * sixAxisUp.y * cam.transform.up.z -
					            sixAxisUp.z * cam.transform.up.y * cam.transform.right.x -
					            cam.transform.up.z * cam.transform.right.y * sixAxisUp.x -
					            cam.transform.right.z * sixAxisUp.y * cam.transform.up.x;
					float angle = Mathf.Atan2(det, dot);

					if (!sceneViewAnimatedRot(sceneView).isAnimating)
					{
						if (Mathf.Abs(angle) > 1.55f)
							sixAxisUp = RoundVector3ToInt(cam.transform.up);
					}
				}

				if (panBehaviour)
				{
					//Rotate camera relative to self
					sceneView.rotation = Quaternion.AngleAxis(e.delta.x * 0.25f * sensitivity, sixAxisUp) * sceneView.rotation;
					sceneView.rotation = sceneView.rotation * Quaternion.AngleAxis(e.delta.y * 0.25f * sensitivity, Vector3.right);
					e.Use();
				}
			}
		}

		private static float MinOrMax(float a, float b)
		{
			return Mathf.Abs(a) > Mathf.Abs(b) ? a : b;
		}

		//zoom-to-cursor variables
		private static Vector3 lastPivot;
		private static RaycastHit hit;
		private static bool didHitOnZoom;

		#region RaycastWorld

		//Legacy raycast world that would hit backfaces. This is the default Unity implementation!
		//It's broken with UI interaction currently. It has been reported as an issue.
		/*private static bool RaycastWorld(Vector2 position, out RaycastHit hit)
		{
			hit = default(RaycastHit);
			object[] args = {position, hit};
			bool b = (bool) raycastWorld.Invoke(null, args);
			hit = (RaycastHit) args[1];
			return b;
		}

		[SerializeField] private static MethodInfo _raycastWorld;

		private static MethodInfo raycastWorld
		{
			get
			{
				return _raycastWorld ?? (_raycastWorld =
					       sceneViewMotionClass.GetMethod("RaycastWorld", BindingFlags.Static | BindingFlags.NonPublic));
			}
		}

		[SerializeField] private static Type _sceneViewMotionClass;

		private static Type sceneViewMotionClass
		{
			get
			{
				return _sceneViewMotionClass ?? (_sceneViewMotionClass = Type.GetType("UnityEditor.SceneViewMotion,UnityEditor"));
			}
		}*/

		[SerializeField] private static MethodInfo _IntersectRayMesh;
		private static MethodInfo IntersectRayMesh
		{
			get
			{
				return _IntersectRayMesh ?? (_IntersectRayMesh =
					       typeof(HandleUtility).GetMethod("IntersectRayMesh", BindingFlags.Static | BindingFlags.NonPublic));
			}
		}

		
		private static bool RaycastWorld(Vector2 position, out RaycastHit hit, bool ignoreBackfaces = true)
		{
			hit = default(RaycastHit);
			Transform cameraTransform = Camera.current.transform;
			GameObject gameObject = HandleUtility.PickGameObject(position, false);
			bool result;
			if (!gameObject)
			{
				result = false;
			}
			else
			{
				Ray ray = HandleUtility.GUIPointToWorldRay(position);
				Vector3 lastPos = cameraTransform.position;
				Quaternion lastRot = cameraTransform.rotation;
				result = IntersectWithRay(ray, gameObject, cameraTransform, out hit, ignoreBackfaces);
				cameraTransform.position = lastPos;
				cameraTransform.rotation = lastRot;
			}
			return result;
		}

		static bool IntersectWithRay(Ray ray, GameObject gameObject, Transform cameraTransform, out RaycastHit hit, bool ignoreBackfaces = true)
		{
			while (true)
			{
				hit = default(RaycastHit);
				MeshFilter[] componentsInChildren = gameObject.GetComponentsInChildren<MeshFilter>();
				float num = float.PositiveInfinity;
				foreach (var meshFilter in componentsInChildren)
				{
					Mesh sharedMesh = meshFilter.sharedMesh;
					if (!sharedMesh) continue;

					object[] parameters = {ray, sharedMesh, meshFilter.transform.localToWorldMatrix, null};
					if ((bool) IntersectRayMesh.Invoke(null, parameters))
					{
						RaycastHit raycastHit = (RaycastHit) parameters[3];
						if (raycastHit.distance < num)
						{
							hit = raycastHit;
							num = hit.distance;
						}
					}
				}

				// ReSharper disable once CompareOfFloatsByEqualityOperator
				if (num == float.PositiveInfinity)
				{
					Collider[] componentsInChildren2 = gameObject.GetComponentsInChildren<Collider>();
					foreach (var collider in componentsInChildren2)
					{
						RaycastHit raycastHit2;
						if (!collider.Raycast(ray, out raycastHit2, float.PositiveInfinity)) continue;
						if (raycastHit2.distance >= num) continue;
						hit = raycastHit2;
						num = hit.distance;
					}
				}

				// ReSharper disable once CompareOfFloatsByEqualityOperator
				if (num == float.PositiveInfinity)
				{
					//If UI
					if (gameObject.GetComponentInParent<Canvas>() != null)
					{
						//Handle interaction with UI by assuming that we've hit a graphic-type element that faces down its own forward axis.
						Transform t = gameObject.transform;
						Plane p = new Plane(t.forward, t.position);
						if (p.Raycast(ray, out num))
						{
							hit.point = ray.origin + ray.direction * num;
							hit.normal = t.forward;
						}
					}
				}

				// ReSharper disable once CompareOfFloatsByEqualityOperator
				if (num == float.PositiveInfinity)
				{
					hit.point = Vector3.Project(gameObject.transform.position - ray.origin, ray.direction) + ray.origin;
					hit.normal = ray.direction;
				}

				if (ignoreBackfaces && Vector3.Dot(cameraTransform.forward, hit.normal) > 0)
				{
					//Hit was backfaces! do the pick logic again
					cameraTransform.position = hit.point;
					cameraTransform.rotation = Quaternion.LookRotation(ray.direction);
					Vector2 position = new Vector2(Camera.current.pixelWidth / 2f, Camera.current.pixelHeight / 2f);
					gameObject = HandleUtility.PickGameObject(position, false);
					if (!gameObject)
					{
						return false;
					}

					ray = HandleUtility.GUIPointToWorldRay(position);
					continue;
				}

				return true;
			}
		}

		#endregion

		private static bool wDown;
		private static bool sDown;
		private static bool aDown;
		private static bool dDown;
		private static bool qDown;
		private static bool eDown;

		private static void OnKeyboardAction(Event e)
		{
			//WSAD keys
			if (e.rawType == EventType.KeyDown)
			{
				switch (e.keyCode)
				{
					case KeyCode.W:
						wDown = true;
						break;
					case KeyCode.S:
						sDown = true;
						break;
					case KeyCode.A:
						aDown = true;
						break;
					case KeyCode.D:
						dDown = true;
						break;
					case KeyCode.Q:
						qDown = true;
						break;
					case KeyCode.E:
						eDown = true;
						break;
				}
			}
			else if (e.rawType == EventType.KeyUp)
			{
				switch (e.keyCode)
				{
					case KeyCode.W:
						wDown = false;
						break;
					case KeyCode.S:
						sDown = false;
						break;
					case KeyCode.A:
						aDown = false;
						break;
					case KeyCode.D:
						dDown = false;
						break;
					case KeyCode.Q:
						qDown = false;
						break;
					case KeyCode.E:
						eDown = false;
						break;
				}
			}
		}

		private static int BoolToInt(bool b)
		{
			return b ? 1 : 0;
		}

		private static void OnRepaint(SceneView sceneView)
		{
			material.SetPass(0);
			GL.PushMatrix();
			GL.LoadPixelMatrix();

			GL.Begin(GL.QUADS);
			int x = (int) (sceneView.camera.pixelWidth - buttonRect.x - buttonRect.width);
			int y = (int) (sceneView.camera.pixelHeight - buttonRect.y);
			DrawBillboardQuad(x, y, (int) buttonRect.width, (int) buttonRect.height);
			GL.End();

			GL.PopMatrix();
			
			
			Handles.BeginGUI();
			string name = ObjectNames.NicifyVariableName(cameraStatus.ToString());
			
			float w = labelStyle.CalcSize(new GUIContent(name)).x;
			GUI.Label(new Rect(x-w/2f+buttonRect.width/2f, sceneView.camera.pixelHeight-y+buttonRect.height, 200, 100), name, labelStyle);
			Handles.EndGUI();
		}

		private static GUIStyle _labelStyle;
		private static GUIStyle labelStyle
		{
			get
			{
				if (_labelStyle != null) return _labelStyle;
				_labelStyle = new GUIStyle("Label") {normal = {textColor = Color.grey}};
				return _labelStyle;
			}
		}

		private static void ContextMenuDelegate(object userData, string[] options, int selected)
		{
			switch (selected)
			{
				case 0:
					cameraStatus = NCameraStatus.off;
					material.mainTexture = textureDefault;
					break;
				case 2:
					cameraStatus = NCameraStatus.trackball;
					material.mainTexture = textureTrackball;
					break;
				case 3:
					cameraStatus = NCameraStatus.sixAxis;
					material.mainTexture = textureSixAxis;
					break;
			}
		}

		#region Rects

		private static Rect _buttonRect;

		private static Rect buttonRect
		{
			get
			{
				// ReSharper disable once CompareOfFloatsByEqualityOperator
				if (_buttonRect.x == 0)
					_buttonRect = new Rect(32, 110, 32, 32);
				return _buttonRect;
			}
		}

		private static Rect GetContextRect(float screenWidth)
		{
			return new Rect(screenWidth - buttonRect.x - buttonRect.width, buttonRect.y, buttonRect.width, buttonRect.height);
		}

		#endregion

		#region Helpers

		// Screen position right at the front (note can't use 1, because even though OSX accepts it Windows doesn't)
		private const float FRONT_Z_DEPTH = 0.99f;

		private static float GetDistanceScalar()
		{
			// ReSharper disable once RedundantAssignment
			float distanceScalar = 1f;
			#if UNITY_5_4_OR_NEWER
			distanceScalar = EditorGUIUtility.pixelsPerPoint;
			#endif
			return distanceScalar;
		}

		private static void DrawBillboardQuad(int x, int y, int width, int height, bool specifiedPoints = true)
		{
			if (specifiedPoints)
			{
				float scale = GetDistanceScalar();
				width = Mathf.RoundToInt(scale * width);
				height = Mathf.RoundToInt(scale * height);
			}

			Vector3 screenPosition = new Vector3(x, y, FRONT_Z_DEPTH);

			GL.TexCoord2(0, 0); // BL
			GL.Vertex(screenPosition + new Vector3(0, -height, 0));
			GL.TexCoord2(1, 0); // BR
			GL.Vertex(screenPosition + new Vector3(width, -height, 0));
			GL.TexCoord2(1, 1); // TR
			GL.Vertex(screenPosition + new Vector3(width, 0, 0));
			GL.TexCoord2(0, 1); // TL
			GL.Vertex(screenPosition + new Vector3(0, 0, 0));
		}

		private static Vector3 RoundVector3ToInt(Vector3 input)
		{
			float x = Mathf.Abs(input.x);
			float y = Mathf.Abs(input.y);
			float z = Mathf.Abs(input.z);
			if (x > y && x > z)
				return new Vector3(Mathf.Sign(input.x), 0, 0);
			if (y > x && y > z)
				return new Vector3(0, Mathf.Sign(input.y), 0);
			return new Vector3(0, 0, Mathf.Sign(input.z));
		}

		#endregion

		#region Materials and Textures

		private static Material _material;

		private static Material material
		{
			get
			{
				if (_material != null) return _material;
				Shader shader = Shader.Find("Particles/Alpha Blended");
				_material = new Material(shader)
				{
					hideFlags = HideFlags.HideAndDontSave,
					shader = {hideFlags = HideFlags.HideAndDontSave},
					mainTexture = textureDefault
				};
				_material.SetColor("_TintColor", new Color(0.75f, 0.75f, 0.75f, 1));

				return _material;
			}
		}

		private static string _basePath;

		private static string basePath
		{
			get
			{
				if (Directory.Exists(_basePath)) return _basePath;

				string suffix = typeof(NCamera).Name;
				
				// Find all the scripts with FreeCamera in their name
				string[] guids = AssetDatabase.FindAssets(string.Format("{0} t:Script", suffix));

				foreach (string guid in guids)
				{
					// Find the path of the file
					string path = AssetDatabase.GUIDToAssetPath(guid);

					// If it is the target file, i.e. FreeCamera.cs not FreeCameraSomething
					if (!path.EndsWith(string.Format("{0}.cs", suffix))) continue;
					path = Path.GetDirectoryName(path);
					path = Path.GetDirectoryName(path);
					path += "/GUI/";
					_basePath = path;
					return path;
				}

				_basePath = string.Empty;
				Debug.LogWarning("path was not found - /GUI above " + suffix + ".cs");

				return _basePath;
			}
		}

		private static Texture2D GetIcon(string name)
		{
			return AssetDatabase.LoadAssetAtPath<Texture2D>(string.Format("{0}.png", Path.Combine(basePath, name)));
		}

		private static Texture2D _textureSixAxis;

		private static Texture2D textureSixAxis
		{
			get
			{
				if (_textureSixAxis == null)
					_textureSixAxis = GetIcon("sixAxis");
				return _textureSixAxis;
			}
		}

		private static Texture2D _textureTrackball;

		private static Texture2D textureTrackball
		{
			get
			{
				if (_textureTrackball == null)
					_textureTrackball = GetIcon("trackball");
				return _textureTrackball;
			}
		}

		private static Texture2D _textureDefault;

		private static Texture2D textureDefault
		{
			get
			{
				if (_textureDefault == null)
					_textureDefault = GetIcon("default");
				return _textureDefault;
			}
		}

		#endregion
	}
}