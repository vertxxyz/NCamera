using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

namespace Vertx
{
	public static class HierarchyDebug
	{
		static Dictionary<Transform, HierarchyDebugWindow> _dict;
		static Dictionary<Transform, HierarchyDebugWindow> dict {
			get {
				if (_dict == null) {
					_dict = new Dictionary<Transform, HierarchyDebugWindow> ();
				} else {
					List<Transform> cleanup = new List<Transform> ();
					foreach (KeyValuePair<Transform, HierarchyDebugWindow> kvp in _dict) {
						if (kvp.Value == null) {
							cleanup.Add (kvp.Key);
						}
					}
					foreach (Transform t in cleanup)
						_dict.Remove (t);
				}
				return _dict;
			}
		}

		/// <summary>
		/// Called to open a window containing the hierarchy associated with the object queried
		/// </summary>
		public static void Log (Transform transform){
			if (transform == null) {
				Debug.LogWarning ("Transform passed to HierarchyDebug was null");
				return;
			}
			Transform root = GetRoot (transform);
			HierarchyDebugWindow hDW;
			if(dict.TryGetValue(root, out hDW)){
				//hDW.Focus ();
			}else{
				hDW = HierarchyDebugWindow.Initialise (root);
				if(hDW != null)
					dict.Add(root, hDW);
			}
		}

		static Transform GetRoot (Transform query) {
			Transform parent = query.parent;
			if (parent == null)
				return query;
			return GetRoot (parent);
		}
	}

	public class HierarchyDebugWindow : EditorWindow
	{
		[SerializeField]
		public Transform transform;

		public static HierarchyDebugWindow Initialise (Transform root) {
			if (root == null) {
				Debug.LogWarning ("Transform passed to HierarchyDebugWindow was null");
				return null;
			}
			HierarchyDebugWindow window = HierarchyDebugWindow.GetWindow<HierarchyDebugWindow> ();
			if (window.m_TreeViewState == null)
				window.m_TreeViewState = new TreeViewState ();
			window.m_HierarchyTreeView = new HierarchyTreeView(window.m_TreeViewState, root);
			GUIContent content = new GUIContent (EditorGUIUtility.IconContent ("UnityEditor.SceneHierarchyWindow"));
			content.text = "Debug";
			window.titleContent = content;
			window.Show ();
			return window;
		}

		void OnGUI () {
			if (m_HierarchyTreeView == null)
				return;
			m_HierarchyTreeView.OnGUI (new Rect(0, 0, position.width, position.height));
		}

		[SerializeField] protected TreeViewState m_TreeViewState;
		protected HierarchyTreeView m_HierarchyTreeView;
		protected class HierarchyTreeView : TreeView {
			Transform root;
			public HierarchyTreeView (TreeViewState state, Transform root) : base (state) {
				this.root = root;
				Reload ();
			}

			protected override TreeViewItem BuildRoot ()
			{
				TreeViewItem rootItem = new TreeViewItem (0, -1, "root");
				if (root == null) {
					rootItem.AddChild (new TreeViewItem{ id = 1, displayName = "null"});
					SetupDepthsFromParentsAndChildren (rootItem);
					return rootItem;
				}
				TreeViewItem rootChild = new TreeViewItem (root.gameObject.GetInstanceID (), -1, root.name);
				rootItem.AddChild(rootChild);

				AddChildrenToItem (rootChild, root);
				SetupDepthsFromParentsAndChildren (rootItem);
				return rootItem;
			}

			void AddChildrenToItem (TreeViewItem item, Transform transform) {
				foreach (Transform child in transform) {
					TreeViewItem childItem = new TreeViewItem {id = child.gameObject.GetInstanceID (), displayName = child.name};
					item.AddChild (childItem);
					AddChildrenToItem (childItem, child);
				}
			}

			protected override void SelectionChanged (IList<int> selectedIds)
			{
				Object[] objects = new Object[selectedIds.Count];
				for(int i = 0; i<selectedIds.Count; i++){
					objects[i] = EditorUtility.InstanceIDToObject (selectedIds[i]);
				}
				Selection.objects = objects;
			}

			protected override void DoubleClickedItem (int id)
			{
				Selection.activeObject = EditorUtility.InstanceIDToObject (id);
			}
		}
	}
}
