using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Darclite.EditorTools
{
    public static class AnimatorControllerBuilder
    {
        private const string ControllerPath = "Assets/_Project/Animations/PlayerAnimatorController.controller";
        private const string ClipsFolder = "Assets/_Project/Animations/Mixamo";

        [MenuItem("Darclite/Create Player Animator Controller")]
        public static void CreateController()
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            {
                AssetDatabase.DeleteAsset(ControllerPath);
            }

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveY", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("DodgeForward", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("DodgeBack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("DodgeLeft", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("DodgeRight", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("IsDodging", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);

            AnimationClip idle = LoadClip("Idle");
            AnimationClip walk = LoadClip("Walk");
            AnimationClip walkBack = LoadClip("WalkBack");
            AnimationClip walkLeft = LoadClip("WalkLeft");
            AnimationClip walkRight = LoadClip("WalkRight");
            AnimationClip run = LoadClip("Run");
            AnimationClip runBack = LoadClip("RunBack");
            AnimationClip runLeft = LoadClip("RunLeft");
            AnimationClip runRight = LoadClip("RunRight");
            AnimationClip jump = LoadClip("Jump");
            AnimationClip dodgeForward = LoadClip("DodgeForward");
            AnimationClip dodgeBack = LoadClip("DodgeBack");
            AnimationClip dodgeLeft = LoadClip("DodgeLeft");
            AnimationClip dodgeRight = LoadClip("DodgeRight");

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;

            BlendTree blendTree = new BlendTree
            {
                name = "Locomotion",
                blendType = BlendTreeType.FreeformCartesian2D,
                blendParameter = "MoveX",
                blendParameterY = "MoveY",
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(blendTree, controller);

            if (idle != null) blendTree.AddChild(idle, Vector2.zero);
            if (walk != null) blendTree.AddChild(walk, new Vector2(0f, 1f));
            if (walkBack != null) blendTree.AddChild(walkBack, new Vector2(0f, -1f));
            if (walkLeft != null) blendTree.AddChild(walkLeft, new Vector2(-1f, 0f));
            if (walkRight != null) blendTree.AddChild(walkRight, new Vector2(1f, 0f));
            if (run != null) blendTree.AddChild(run, new Vector2(0f, 2f));
            if (runBack != null) blendTree.AddChild(runBack, new Vector2(0f, -2f));
            if (runLeft != null) blendTree.AddChild(runLeft, new Vector2(-2f, 0f));
            if (runRight != null) blendTree.AddChild(runRight, new Vector2(2f, 0f));

            AnimatorState locomotionState = stateMachine.AddState("Locomotion");
            locomotionState.motion = blendTree;
            stateMachine.defaultState = locomotionState;

            AnimatorState jumpState = stateMachine.AddState("Jump");
            jumpState.motion = jump;
            jumpState.speed = 1.5f;

            AnimatorStateTransition toJump = locomotionState.AddTransition(jumpState);
            toJump.hasExitTime = false;
            toJump.duration = 0.1f;
            toJump.AddCondition(AnimatorConditionMode.If, 0, "Jump");

            AnimatorStateTransition toLocomotion = jumpState.AddTransition(locomotionState);
            toLocomotion.hasExitTime = true;
            toLocomotion.exitTime = 0.95f;
            toLocomotion.duration = 0.15f;

            AddDodgeState(stateMachine, locomotionState, "Dodge Forward", "DodgeForward", dodgeForward);
            AddDodgeState(stateMachine, locomotionState, "Dodge Back", "DodgeBack", dodgeBack);
            AddDodgeState(stateMachine, locomotionState, "Dodge Left", "DodgeLeft", dodgeLeft);
            AddDodgeState(stateMachine, locomotionState, "Dodge Right", "DodgeRight", dodgeRight);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            Debug.Log($"Player Animator Controller created at {ControllerPath}");
        }

        private static void AddDodgeState(AnimatorStateMachine stateMachine, AnimatorState locomotionState, string stateName, string triggerParameter, AnimationClip clip)
        {
            if (clip == null)
            {
                return;
            }

            AnimatorState dodgeState = stateMachine.AddState(stateName);
            dodgeState.motion = clip;
            dodgeState.speed = 1.3f;

            AnimatorStateTransition toDodge = locomotionState.AddTransition(dodgeState);
            toDodge.hasExitTime = false;
            toDodge.duration = 0.05f;
            toDodge.AddCondition(AnimatorConditionMode.If, 0, triggerParameter);

            AnimatorStateTransition toLocomotion = dodgeState.AddTransition(locomotionState);
            toLocomotion.hasExitTime = true;
            toLocomotion.exitTime = 0.9f;
            toLocomotion.duration = 0.1f;

            // Early exit: once the physical dash ends, if the player is still holding a
            // movement key, skip the rest of the dodge's settle/recovery pose and blend
            // straight back into locomotion instead of waiting out the fixed exit time.
            AnimatorStateTransition toLocomotionEarly = dodgeState.AddTransition(locomotionState);
            toLocomotionEarly.hasExitTime = false;
            toLocomotionEarly.duration = 0.15f;
            toLocomotionEarly.AddCondition(AnimatorConditionMode.IfNot, 0, "IsDodging");
            toLocomotionEarly.AddCondition(AnimatorConditionMode.If, 0, "IsMoving");
        }

        private static AnimationClip LoadClip(string name)
        {
            string path = $"{ClipsFolder}/{name}.fbx";
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (Object asset in assets)
            {
                if (asset is AnimationClip clip && !clip.name.Contains("__preview__"))
                {
                    return clip;
                }
            }

            Debug.LogWarning($"[AnimatorControllerBuilder] Could not find animation clip at {path}");
            return null;
        }
    }
}
