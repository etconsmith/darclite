using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Darclite.EditorTools
{
    public static class AnimatorControllerBuilder
    {
        private const string ControllerPath = "Assets/_Project/Animations/PlayerAnimatorController.controller";
        private const string ClipsFolder = "Assets/_Project/Animations/Mixamo";
        private const string FightClipsFolder = "Assets/_Project/Animations/FightAnimations";

        // Also used by SceneBootstrapper to scale the attack-cooldown/stun durations it derives
        // from raw clip lengths, so gameplay pacing matches the sped-up animator playback.
        public const float AttackSpeedMultiplier = 1.5f * 1.3f * 1.5f;
        public const float HitSpeedMultiplier = 2f;

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
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("AttackIndex", AnimatorControllerParameterType.Float);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("HitIndex", AnimatorControllerParameterType.Float);
            controller.AddParameter("Knockback", AnimatorControllerParameterType.Trigger);

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

            AnimationClip bodyPunchLeft = LoadClip(FightClipsFolder, "BodyPunchLeft");
            AnimationClip bodyPunchRight = LoadClip(FightClipsFolder, "BodyPunchRight");
            AnimationClip headPunchLeft = LoadClip(FightClipsFolder, "HeadPunchLeft");
            AnimationClip headPunchRight = LoadClip(FightClipsFolder, "HeadPunchRight");
            AnimationClip bodyPunchLeft2 = LoadClip(FightClipsFolder, "BodyPunchLeft2");
            AnimationClip bodyPunchRight2 = LoadClip(FightClipsFolder, "BodyPunchRight2");
            AnimationClip headPunchLeft2 = LoadClip(FightClipsFolder, "HeadPunchLeft2");
            AnimationClip headPunchRight2 = LoadClip(FightClipsFolder, "HeadPunchRight2");
            AnimationClip headHeavyLeft = LoadClip(FightClipsFolder, "HeadHeavyLeft");
            AnimationClip headHeavyRight = LoadClip(FightClipsFolder, "HeadHeavyRight");
            AnimationClip bodyHitLeft = LoadClip(FightClipsFolder, "BodyHitLeft");
            AnimationClip bodyHitRight = LoadClip(FightClipsFolder, "BodyHitRight");
            AnimationClip headHitLeft = LoadClip(FightClipsFolder, "HeadHitLeft");
            AnimationClip headHitRight = LoadClip(FightClipsFolder, "HeadHitRight");
            AnimationClip knockback = LoadClip(FightClipsFolder, "Knockback");

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

            BlendTree attackTree = new BlendTree
            {
                name = "Attack Blend",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "AttackIndex",
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(attackTree, controller);

            if (bodyPunchLeft != null) attackTree.AddChild(bodyPunchLeft, 0f);
            if (bodyPunchRight != null) attackTree.AddChild(bodyPunchRight, 1f);
            if (headPunchLeft != null) attackTree.AddChild(headPunchLeft, 2f);
            if (headPunchRight != null) attackTree.AddChild(headPunchRight, 3f);
            if (bodyPunchLeft2 != null) attackTree.AddChild(bodyPunchLeft2, 4f);
            if (bodyPunchRight2 != null) attackTree.AddChild(bodyPunchRight2, 5f);
            if (headPunchLeft2 != null) attackTree.AddChild(headPunchLeft2, 6f);
            if (headPunchRight2 != null) attackTree.AddChild(headPunchRight2, 7f);
            if (headHeavyLeft != null) attackTree.AddChild(headHeavyLeft, 8f);
            if (headHeavyRight != null) attackTree.AddChild(headHeavyRight, 9f);

            AnimatorState attackState = stateMachine.AddState("Attack");
            attackState.motion = attackTree;
            attackState.speed = AttackSpeedMultiplier;

            AnimatorStateTransition toAttack = locomotionState.AddTransition(attackState);
            toAttack.hasExitTime = false;
            toAttack.duration = 0.05f;
            toAttack.AddCondition(AnimatorConditionMode.If, 0, "Attack");

            AnimatorStateTransition attackToLocomotion = attackState.AddTransition(locomotionState);
            attackToLocomotion.hasExitTime = true;
            attackToLocomotion.exitTime = 0.9f;
            attackToLocomotion.duration = 0.1f;

            BlendTree hitTree = new BlendTree
            {
                name = "Hit Blend",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "HitIndex",
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(hitTree, controller);

            if (bodyHitLeft != null) hitTree.AddChild(bodyHitLeft, 0f);
            if (bodyHitRight != null) hitTree.AddChild(bodyHitRight, 1f);
            if (headHitLeft != null) hitTree.AddChild(headHitLeft, 2f);
            if (headHitRight != null) hitTree.AddChild(headHitRight, 3f);

            AnimatorState hitState = stateMachine.AddState("Hit");
            hitState.motion = hitTree;
            hitState.speed = HitSpeedMultiplier;

            AnimatorStateTransition toHit = stateMachine.AddAnyStateTransition(hitState);
            toHit.hasExitTime = false;
            toHit.duration = 0.05f;
            // Getting hit again while already in the Hit state should restart the reaction from
            // its impact frame (matching the new HitIndex) instead of continuing wherever the
            // previous hit's playback was.
            toHit.canTransitionToSelf = true;
            toHit.AddCondition(AnimatorConditionMode.If, 0, "Hit");

            AnimatorStateTransition hitToLocomotion = hitState.AddTransition(locomotionState);
            hitToLocomotion.hasExitTime = true;
            hitToLocomotion.exitTime = 0.9f;
            hitToLocomotion.duration = 0.1f;

            AnimatorState knockbackState = stateMachine.AddState("Knockback");
            knockbackState.motion = knockback;

            AnimatorStateTransition toKnockback = stateMachine.AddAnyStateTransition(knockbackState);
            toKnockback.hasExitTime = false;
            toKnockback.duration = 0.05f;
            toKnockback.canTransitionToSelf = false;
            toKnockback.AddCondition(AnimatorConditionMode.If, 0, "Knockback");

            AnimatorStateTransition knockbackToLocomotion = knockbackState.AddTransition(locomotionState);
            knockbackToLocomotion.hasExitTime = true;
            knockbackToLocomotion.exitTime = 0.9f;
            knockbackToLocomotion.duration = 0.15f;

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
            return LoadClip(ClipsFolder, name);
        }

        private static AnimationClip LoadClip(string folder, string name)
        {
            string path = $"{folder}/{name}.fbx";
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
