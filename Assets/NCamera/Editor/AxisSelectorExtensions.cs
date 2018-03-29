/* 2018-01-03 | Thomas Ingram */
using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;
using UnityEditor.AnimatedValues;

public static class AxisSelectorExtensions {
#region Axis Selectors

	public static void AxisSelectorGUI(SceneView sceneView, Action<Quaternion> Callback)
	{
		AxisSelectorGUI(sceneView, Vector3.zero, Callback);
	}
	
	public static void AxisSelectorGUI (SceneView sceneView, Vector3 highlightDir, Action<Quaternion> Callback)
	{
		Handles.matrix = Matrix4x4.identity;
		Camera camera = sceneView.camera;
		SetCameraFilterMode(camera, 0);
		bool glSRGB = GL.sRGBWrite;
		GL.sRGBWrite = false;

		HandleUtility.PushCamera(camera);
		if (camera.orthographic)
		{
			camera.orthographicSize = 0.5f;
		}
		camera.cullingMask = 0;
		camera.transform.position = camera.transform.rotation * new Vector3(0f, 0f, -5f);
		camera.clearFlags = CameraClearFlags.Nothing;
		camera.nearClipPlane = 0.1f;
		camera.farClipPlane = 10f;
		camera.fieldOfView = m_Ortho.Fade(70f, 0f);
		AddCursorRect(new Rect(sceneView.position.width - 100f + 22f, 22f, 56f, 102f), MouseCursor.Arrow);
		Handles.SetCamera(new Rect(sceneView.position.width - 103.5f, 3f, 100f, 100f), camera);
		float num2 = HandleUtility.GetHandleSize(Vector3.zero) * 0.2f;
		
		//Draw the axis selectors behind the cube
		AxisSelectors(sceneView, camera, num2, -1f, viewAxisLabelStyle, highlightDir, Callback);

		//Draw the center cube
		Color color = Handles.centerColor;
		color.a *= fadedRotationLock;
		if (color.a <= 0.1f || sceneView.isRotationLocked)
		{
			GUI.enabled = false;
		}
		Handles.color = color;
		int id = m_CenterButtonControlID;
		Vector3 pos = Vector3.zero;
		Quaternion rot = Quaternion.identity;
		float size = num2 * 0.8f;
		float pickSize = num2;

		if (Button(id, pos, rot, size, pickSize, Handles.CubeHandleCap) && !sceneView.in2DMode && !sceneView.isRotationLocked)
		{
			if (Event.current.clickCount == 2)
			{
				sceneView.FrameSelected();
			}
			else if (Event.current.shift || Event.current.button == 2)
			{
				ViewFromNiceAngle(sceneView, true);
			}
			else
			{
				ViewSetOrtho(sceneView, !sceneView.orthographic);
			}
		}
		
		//Draw the axis selectors in front of the cube
		AxisSelectors(sceneView, camera, num2, 1f, viewAxisLabelStyle, highlightDir, Callback);
		HandleUtility.PopCamera(camera);
		Handles.SetCamera(camera);
		GL.sRGBWrite = glSRGB;
	}

	private static MethodInfo _SetCameraFilterMode;

	private static void SetCameraFilterMode (Camera camera, int filterMode){
		if (_SetCameraFilterMode == null)
			_SetCameraFilterMode = typeof(Handles).GetMethod ("SetCameraFilterMode", BindingFlags.Static | BindingFlags.NonPublic);
		if (_SetCameraFilterMode == null)
		{
			Debug.LogWarning("_SetCameraFilterMode was found to be null, Unity Update has likely refactored internal code");
			return;
		}
		_SetCameraFilterMode.Invoke (null, new object[]{camera, filterMode});
	}

	private static FieldInfo _m_Visible;

	public static void ShowOrHideOtherAxisSelectors (bool show){
		if (_m_Visible == null)
			_m_Visible = sceneViewRotationClass.GetField ("m_Visible", BindingFlags.NonPublic | BindingFlags.Instance);
		if (_svRot == null)
			_svRot = typeof(SceneView).GetField ("svRot", BindingFlags.NonPublic | BindingFlags.Instance);
		AnimBool aB = ((AnimBool)_m_Visible.GetValue (_svRot.GetValue (SceneView.lastActiveSceneView)));
		aB.value = show; 
	}

	private static MethodInfo _ViewFromNiceAngle;

	private static void ViewFromNiceAngle(SceneView sceneView, bool value){
		if (_ViewFromNiceAngle == null)
			_ViewFromNiceAngle = typeof(SceneView).GetMethod ("ViewFromNiceAngle", BindingFlags.NonPublic | BindingFlags.Instance);
		_ViewFromNiceAngle.Invoke (null, new object[]{ sceneView, value });
	}

	private static MethodInfo _ViewSetOrtho;

	private static void ViewSetOrtho(SceneView sceneView, bool value){
		if (_ViewSetOrtho == null)
			_ViewSetOrtho = sceneViewRotationClass.GetMethod ("ViewSetOrtho", BindingFlags.NonPublic | BindingFlags.Instance);
		if (_svRot == null)
			_svRot = typeof(SceneView).GetField ("svRot", BindingFlags.NonPublic | BindingFlags.Instance);
		_ViewSetOrtho.Invoke (_svRot.GetValue (SceneView.lastActiveSceneView), new object[]{ sceneView, value });
	}

	private static MethodInfo _AddCursorRect;

	private static void AddCursorRect(Rect r, MouseCursor mC){
		if (_AddCursorRect == null)
			_AddCursorRect = typeof(SceneView).GetMethod ("AddCursorRect", BindingFlags.Static | BindingFlags.NonPublic);
		_AddCursorRect.Invoke (null, new object[]{ r, mC });
	}

	private static FieldInfo _dirVisible;
	private static FieldInfo _svRot;

	private static AnimBool[] dirVisible {
		get {
			if(_dirVisible == null)
				_dirVisible = sceneViewRotationClass.GetField ("dirVisible", BindingFlags.NonPublic | BindingFlags.Instance);
			if (_svRot == null)
				_svRot = typeof(SceneView).GetField ("svRot", BindingFlags.NonPublic | BindingFlags.Instance);
			return (AnimBool[])_dirVisible.GetValue (_svRot.GetValue (SceneView.lastActiveSceneView));
		}
	}

	private static FieldInfo _m_Ortho;

	private static AnimBool m_Ortho {
		get {
			if(_m_Ortho == null)
				_m_Ortho = typeof(SceneView).GetField ("m_Ortho", BindingFlags.NonPublic | BindingFlags.Instance);
			return (AnimBool)_m_Ortho.GetValue (SceneView.lastActiveSceneView);
		}
	}

	private static PropertyInfo _fadedRotationLock;

	private static float fadedRotationLock {
		get {
			if(_fadedRotationLock == null)
				_fadedRotationLock = sceneViewRotationClass.GetProperty ("fadedRotationLock", BindingFlags.NonPublic | BindingFlags.Instance);
			if (_svRot == null)
				_svRot = typeof(SceneView).GetField ("svRot", BindingFlags.NonPublic | BindingFlags.Instance);
			return (float)_fadedRotationLock.GetValue(_svRot.GetValue (SceneView.lastActiveSceneView), null);
		}
	}

	[SerializeField] private static Type _sceneViewRotationClass;

	private static Type sceneViewRotationClass {
		get {
			if (_sceneViewRotationClass == null)
				_sceneViewRotationClass = Type.GetType ("UnityEditor.SceneViewRotation,UnityEditor");
			return _sceneViewRotationClass;
		}
	}

	private static readonly Quaternion[] axisRotations = {
		Quaternion.LookRotation (new Vector3(-1f, 0f, 0f)),
		Quaternion.LookRotation (new Vector3(0f, -1f, 0f)),
		Quaternion.LookRotation (new Vector3(0f, 0f, -1f)),
		Quaternion.LookRotation (new Vector3(1f, 0f, 0f)),
		Quaternion.LookRotation (new Vector3(0f, 1f, 0f)),
		Quaternion.LookRotation (new Vector3(0f, 0f, 1f))
	};

	private static readonly Color colorInvisible = new Color(1,1,1,0);

	private static int[] _m_ViewDirectionControlIDs;

	private static int[] m_ViewDirectionControlIDs{
		get{
			if (_m_ViewDirectionControlIDs == null) {
				_m_ViewDirectionControlIDs = new int[axisRotations.Length];
				for (int l = 0; l < _m_ViewDirectionControlIDs.Length; l++) {
					_m_ViewDirectionControlIDs [l] = GetPermanentControlID ();
				}
			}
			return _m_ViewDirectionControlIDs;
		}
	}

	private static int _m_CenterButtonControlID;

	private static int m_CenterButtonControlID {
		get {
			if (_m_CenterButtonControlID == 0)
				_m_CenterButtonControlID = GetPermanentControlID ();
			return _m_CenterButtonControlID;
		}
	}

	private static int GetPermanentControlID () {
		if (_GetPermanentControlID == null)
			_GetPermanentControlID = typeof(GUIUtility).GetMethod ("GetPermanentControlID", BindingFlags.Static | BindingFlags.NonPublic);
		return (int)_GetPermanentControlID.Invoke (null, null);
	}

	private static MethodInfo _GetPermanentControlID;

	private static readonly GUIContent[] s_HandleAxisLabels = {
		new GUIContent("x"),
		new GUIContent("y"),
		new GUIContent("z")
	};

	private static GUIStyle viewAxisLabelStyle {
		get {
			if(_viewAxisLabelStyle==null)
				_viewAxisLabelStyle = "SC ViewAxisLabel";
			return _viewAxisLabelStyle;
		}
	}

	private static GUIStyle _viewAxisLabelStyle;

	private static bool Button (int controlID, Vector3 pos, Quaternion dir, float size, float pickSize, Handles.CapFunction cF){
		if(_Button == null)
			_Button = typeof(Handles).GetMethod("Button", BindingFlags.Static | BindingFlags.NonPublic, null, new[]{typeof(int), typeof(Vector3), typeof(Quaternion), typeof(float), typeof(float), typeof(Handles.CapFunction)}, null);
		return (bool)_Button.Invoke (null, new object[]{controlID, pos, dir, size, pickSize, cF});
	}

	private static MethodInfo _Button;

	//Draw axis selectors with the dot sign of camera forward matching sgn
	private static void AxisSelectors(SceneView view, Camera cam, float size, float sgn, GUIStyle viewAxisLabelStyle, Vector3 highlightDir, Action<Quaternion> Callback)
	{
		for (int i = axisRotations.Length - 1; i >= 0; i--)
		{
			Quaternion quaternion = axisRotations[i];
			float faded = dirVisible[i % 3].faded;
			Vector3 vector = quaternion * Vector3.forward;
			float num = Vector3.Dot(view.camera.transform.forward, vector);
			if (num > 0.0 || sgn <= 0f)
			{
				if (num <= 0.0 || sgn >= 0f)
				{
					Color color;
					switch (i)
					{
					case 0:
						color = Color.Lerp(Handles.xAxisColor, Color.white, 0.25f);
						break;
					case 1:
						color = Color.Lerp(Handles.yAxisColor, Color.white, 0.25f);
						break;
					case 2:
						color = Color.Lerp(Handles.zAxisColor, Color.white, 0.25f);
						break;
					default:
						color = Handles.centerColor;
						break;
					}
					color = Color.Lerp(color, Color.yellow, 0.4f * Math.Max(0, Vector3.Dot(highlightDir, -vector)));
					if (view.in2DMode)
						color = colorInvisible;
					//used to be *fadedVisibility but has changed to fadedRotationLock as fadedVisiblity contains a visibility float that we're overriding to disable the original axis selector
					color.a *= faded * fadedRotationLock;
					Handles.color = color;
					if (color.a <= 0.1f || view.isRotationLocked)
						GUI.enabled = false;
					if (sgn > 0f)
					{
						int id = m_ViewDirectionControlIDs[i];
						Vector3 pos = quaternion * Vector3.forward * size * -1.2f;
						Quaternion rot = quaternion;
						float pickSize = size * 0.7f;
						if (Button(id, pos, rot, size, pickSize, Handles.ConeHandleCap))
						{
							if (!view.in2DMode && !view.isRotationLocked)
							{
								Callback.Invoke(quaternion);
							}
						}
					}
					if (i < 3)
					{
						GUI.color = new Color(1f, 1f, 1f, dirVisible[i].faded);
						Vector3 a = vector;
						a += num * view.camera.transform.forward * -0.5f;
						a = (a * 0.7f + a.normalized * 1.5f) * size;
						Handles.Label(-a, s_HandleAxisLabels[i], viewAxisLabelStyle);
					}
					if (sgn < 0f)
					{
						int id = m_ViewDirectionControlIDs[i];
						Vector3 pos = quaternion * Vector3.forward * size * -1.2f;
						Quaternion rot = quaternion;
						float pickSize = size * 0.7f;
						if (Button(id, pos, rot, size, pickSize, Handles.ConeHandleCap))
						{
							if (!view.in2DMode && !view.isRotationLocked)
							{
								Callback.Invoke(quaternion);
							}
						}
					}
					Handles.color = Color.white;
					GUI.color = Color.white;
					GUI.enabled = true;
				}
			}
		}
	}
	#endregion
}
