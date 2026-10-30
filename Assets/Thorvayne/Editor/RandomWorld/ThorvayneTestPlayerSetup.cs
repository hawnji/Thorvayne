using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thorvayne.WorldGen.EditorTools
{
    // Menu: Thorvayne > Add Test Player
    // Puts a walkable third-person test character at the world's PlayerSpawn. Press Play to walk around.
    // The random world generator calls this automatically after each build.
    public static class ThorvayneTestPlayerSetup
    {
        const string PlayerName = "Test Player";
        const string HeroPath = "Assets/Thorvayne/Characters/ThorvayneHero.fbx";
        const string ControllerPath = "Assets/Thorvayne/Characters/ThorvayneHero.controller";
        const string GripMaskPath = "Assets/Thorvayne/Characters/ThorvayneHero_RightFingers.mask";
        const string SkinPath = "Assets/Thorvayne/Characters/ThorvayneHero_Skin.png";   // optional colour texture
        const string AxeWoodPath = "Assets/Thorvayne/Characters/ThorvayneAxe_Albedo.png";
        const string AxeMetalPath = "Assets/Thorvayne/Characters/ThorvayneAxe_Metal.png";   // light grey, tinted per axe in game
        static readonly string[] Clips = { "Idle", "Walk", "Run", "Jump", "Swim", "Fly", "Chop" };
        const float HeroHeight = 1.8f;

        [MenuItem("Thorvayne/Add Test Player")]
        static void AddFromMenu()
        {
            var spawn = GameObject.Find("Thorvayne Random World/PlayerSpawn") ?? GameObject.Find("PlayerSpawn");
            if (spawn == null)
            {
                EditorUtility.DisplayDialog("Thorvayne", "No PlayerSpawn found. Generate a world first (Thorvayne > Random World Generator).", "OK");
                return;
            }
            var sea = GameObject.Find("Thorvayne Random World/Sea") ?? GameObject.Find("Sea");
            var player = Setup(spawn.transform.position, sea != null ? sea.transform.position.y : -10000f);
            Selection.activeGameObject = player;
        }

        [MenuItem("Thorvayne/Reset Test Player Progress")]
        static void ResetProgress()
        {
            if (!EditorUtility.DisplayDialog("Thorvayne", "Clear the test player's skill levels, inventory and equipment?", "Clear", "Cancel")) return;
            ThorvayneSkills.ClearSave();
            ThorvayneInventory.ClearSave();
            ThorvayneEquipment.ClearSave();
            PlayerPrefs.Save();
        }

        public static GameObject Setup(Vector3 position, float seaLevel)
        {
            var old = GameObject.Find(PlayerName);
            if (old != null) Undo.DestroyObjectImmediate(old);

            var player = new GameObject(PlayerName);
            player.transform.position = position + Vector3.up * 0.2f;
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.9f; cc.radius = 0.4f; cc.center = new Vector3(0, 0.95f, 0);
            cc.slopeLimit = 50f; cc.stepOffset = 0.5f;

            if (!AddHeroModel(player))
            {
                // placeholder body: a capsule with a "visor" box so you can see which way it faces
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body";
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.transform.SetParent(player.transform, false);
                body.transform.localPosition = new Vector3(0, 0.95f, 0);
                body.transform.localScale = new Vector3(0.8f, 0.95f, 0.8f);
                body.GetComponent<MeshRenderer>().sharedMaterial = PlayerMat("PlayerBody", new Color(0.2f, 0.45f, 0.85f));
                var visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visor.name = "Visor";
                Object.DestroyImmediate(visor.GetComponent<Collider>());
                visor.transform.SetParent(player.transform, false);
                visor.transform.localPosition = new Vector3(0, 1.55f, 0.33f);
                visor.transform.localScale = new Vector3(0.5f, 0.15f, 0.2f);
                visor.GetComponent<MeshRenderer>().sharedMaterial = PlayerMat("PlayerVisor", new Color(1f, 0.85f, 0.3f));
            }

            var p = player.AddComponent<ThorvaynePlayer>();
            p.seaLevel = seaLevel;
            p.cam = MainCamera();
            // gameplay: skills, inventory, on-screen UI, woodcutting
            player.AddComponent<ThorvayneSkills>();
            player.AddComponent<ThorvayneInventory>();
            player.AddComponent<ThorvayneEquipment>();
            player.AddComponent<ThorvayneGameUI>();
            player.AddComponent<ThorvayneWoodcutting>();

            Undo.RegisterCreatedObjectUndo(player, "Add Test Player");
            EditorSceneManager.MarkSceneDirty(player.scene);
            return player;
        }

        // ---------------- animated character ----------------
        // Uses Characters/ThorvayneHero.fbx when it is in the project; otherwise the capsule placeholder is used.
        static bool AddHeroModel(GameObject player)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(HeroPath);
            if (model == null) return false;
            EnsureImportSettings();
            var controller = BuildController();
            if (controller == null) return false;

            var body = (GameObject)PrefabUtility.InstantiatePrefab(model);
            body.name = "Body";
            body.transform.SetParent(player.transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;

            // face the player's forward (+Z): square to the line through the hips, on the side the toes point to.
            // (Not along one foot: feet turn out ~10 degrees, which made the character run slightly sideways.)
            var bodyAnim = body.GetComponent<Animator>();
            bool human = bodyAnim != null && bodyAnim.isHuman;
            var foot = human ? bodyAnim.GetBoneTransform(HumanBodyBones.LeftFoot) : FindDeep(body.transform, "LeftFoot");
            var toes = human ? bodyAnim.GetBoneTransform(HumanBodyBones.LeftToes) : FindDeep(body.transform, "LeftToes");
            var legL = human ? bodyAnim.GetBoneTransform(HumanBodyBones.LeftUpperLeg) : FindDeep(body.transform, "LeftUpLeg");
            var legR = human ? bodyAnim.GetBoneTransform(HumanBodyBones.RightUpperLeg) : FindDeep(body.transform, "RightUpLeg");
            if (foot != null && toes != null)
            {
                var d = toes.position - foot.position; d.y = 0;
                if (legL != null && legR != null)
                {
                    var right = legR.position - legL.position; right.y = 0;
                    var f = Vector3.Cross(right, Vector3.up);
                    if (f.sqrMagnitude > 1e-6f) d = Vector3.Dot(f, d) >= 0 ? f : -f;
                }
                if (d.sqrMagnitude > 1e-6f)
                    body.transform.localRotation = Quaternion.FromToRotation(d.normalized, Vector3.forward);
            }
            // scale to a 1.8 m tall character standing on the ground
            // (measured from the meshes' bind-pose bounds: a skinned renderer's live bounds aren't reliable in edit mode)
            // the axe (Extras/add_axe.py) sits in the right hand; keep it out of sizing and skin
            var rends = body.GetComponentsInChildren<Renderer>(true).Where(r => r.name != "Axe").ToArray();
            if (MeshWorldBounds(rends, out var b) && b.size.y > 1e-5f)
            {
                body.transform.localScale *= HeroHeight / b.size.y;
                MeshWorldBounds(rends, out b);
                // the CharacterController hovers by its skin width, so sink the model by the same amount
                float skin = player.GetComponent<CharacterController>().skinWidth;
                body.transform.position += Vector3.up * (player.transform.position.y - b.min.y - skin);
            }
            var mat = TexturedMat("HeroSkin", SkinPath, new Color(0.78f, 0.66f, 0.55f));
            foreach (var r in rends)
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }

            var axe = FindDeep(body.transform, "Axe");
            if (axe != null)
            {
                var ar = axe.GetComponent<Renderer>();
                if (ar != null)
                {
                    var metal = TexturedMat("AxeMetal", AxeMetalPath, new Color(0.62f, 0.63f, 0.66f));
                    if (metal.HasProperty("_Metallic")) metal.SetFloat("_Metallic", 0.5f);
                    if (metal.HasProperty("_Smoothness")) metal.SetFloat("_Smoothness", 0.45f);
                    ar.sharedMaterials = new[] { TexturedMat("AxeWood", AxeWoodPath, new Color(0.45f, 0.3f, 0.15f)), metal };
                }
                axe.gameObject.SetActive(false);   // shown while an axe is equipped or in use
            }

            var animator = body.GetComponent<Animator>() ?? body.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var ca = player.AddComponent<ThorvayneCharacterAnimator>();
            ca.animator = animator;
            return true;
        }

        // Humanoid rig (works with Mixamo and Asset Store animations), looping clips named Idle/Walk/Run/Jump/Swim/Fly.
        static void EnsureImportSettings()
        {
            var importer = AssetImporter.GetAtPath(HeroPath) as ModelImporter;
            if (importer == null) return;
            bool changed = false;
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                changed = true;
            }
            if (!importer.importAnimation) { importer.importAnimation = true; changed = true; }
            var clips = importer.clipAnimations;
            // redo it when the FBX gained clips (e.g. Chop) or a clip changed length (e.g. a new Mixamo Idle)
            var defaults = importer.defaultClipAnimations;
            bool lengthsChanged = clips != null && clips.Any(c => !defaults.Any(d => d.takeName == c.takeName && d.lastFrame == c.lastFrame));
            if (changed || clips == null || clips.Length == 0 || clips.Length != defaults.Length || lengthsChanged
                || clips.Any(c => !c.loopTime || !c.heightFromFeet || c.name.Contains("|")))
            {
                clips = importer.defaultClipAnimations;
                foreach (var c in clips)
                {
                    int bar = c.name.LastIndexOf('|');
                    if (bar >= 0) c.name = c.name.Substring(bar + 1);
                    c.loopTime = true;
                    c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true;
                    c.heightFromFeet = true;   // Root Y based on feet: keeps the soles on the ground
                }
                importer.clipAnimations = clips;
                changed = true;
            }
            if (changed) importer.SaveAndReimport();
        }

        static AnimatorController BuildController()
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(HeroPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToList();
            if (clips.Count == 0) { Debug.LogWarning("ThorvayneHero.fbx has no animation clips"); return null; }
            AssetDatabase.DeleteAsset(ControllerPath);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var sm = ac.layers[0].stateMachine;
            foreach (var name in Clips)
            {
                var clip = clips.FirstOrDefault(c => c.name == name || c.name.EndsWith("|" + name));
                if (clip == null) { Debug.LogWarning($"ThorvayneHero.fbx is missing the {name} clip"); continue; }
                var st = sm.AddState(name);
                st.motion = clip;
                if (name == "Idle") sm.defaultState = st;
            }

            // "Grip" layer: closes the right hand around the axe. It only moves the right fingers, and
            // ThorvayneWoodcutting turns it on while an axe is in hand (Extras/add_grip.py makes the clip).
            var grip = clips.FirstOrDefault(c => c.name == "Grip" || c.name.EndsWith("|Grip"));
            if (grip != null)
            {
                AssetDatabase.DeleteAsset(GripMaskPath);
                var mask = new AvatarMask();
                for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                    mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, (AvatarMaskBodyPart)i == AvatarMaskBodyPart.RightFingers);
                AssetDatabase.CreateAsset(mask, GripMaskPath);
                ac.AddLayer("Grip");
                var layers = ac.layers;
                var gl = layers[layers.Length - 1];
                gl.avatarMask = mask;
                gl.defaultWeight = 0f;
                gl.stateMachine.defaultState = gl.stateMachine.AddState("Grip");
                gl.stateMachine.defaultState.motion = grip;
                ac.layers = layers;
            }
            else Debug.LogWarning("ThorvayneHero.fbx is missing the Grip clip (the hand stays open around the axe)");
            AssetDatabase.SaveAssets();
            return ac;
        }

        static bool MeshWorldBounds(Renderer[] rends, out Bounds result)
        {
            result = new Bounds(); bool any = false;
            foreach (var r in rends)
            {
                Mesh mesh = null;
                if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
                if (mesh == null) continue;
                var mb = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var w = r.transform.TransformPoint(corner);
                    if (!any) { result = new Bounds(w, Vector3.zero); any = true; } else result.Encapsulate(w);
                }
            }
            return any;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name || t.name.EndsWith(":" + name)) return t;
            foreach (Transform c in t) { var r = FindDeep(c, name); if (r != null) return r; }
            return null;
        }

        static Camera MainCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
                Undo.RegisterCreatedObjectUndo(go, "Add Camera");
            }
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 4000f;
            return cam;
        }

        // a material with an optional colour texture (plain colour when the texture isn't there)
        static Material TexturedMat(string name, string texPath, Color fallback)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            var mat = PlayerMat(name, tex != null ? Color.white : fallback);
            foreach (var prop in new[] { "_BaseMap", "_MainTex", "_BaseColorMap" })   // URP, Built-in, HDRP
                if (mat.HasProperty(prop)) mat.SetTexture(prop, tex);
            return mat;
        }

        static Material PlayerMat(string name, Color color)
        {
            string dir = "Assets/Thorvayne/Generated";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Thorvayne", "Generated");
            string path = $"{dir}/Mat_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var rp = GraphicsSettings.currentRenderPipeline;
                string shader = rp == null ? "Standard" : rp.GetType().Name.Contains("HD") ? "HDRP/Lit" : "Universal Render Pipeline/Lit";
                mat = new Material(Shader.Find(shader) ?? Shader.Find("Standard"));
                AssetDatabase.CreateAsset(mat, path);
            }
            if (mat.HasProperty("_Color")) mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
