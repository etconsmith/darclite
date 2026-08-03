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
        private const string LiteClipsFolder = "Assets/_Project/Animations/Lite Animations";

        // Also used by SceneBootstrapper to scale the attack-cooldown/stun durations it derives
        // from raw clip lengths, so gameplay pacing matches the sped-up animator playback.
        // Reverted back to the raw, unscaled clip speed.
        public const float AttackSpeedMultiplier = 1f;
        public const float HitSpeedMultiplier = 2f;

        // Also used by SceneBootstrapper/Combatant so the Knockback state's real playback time
        // exactly matches Combatant's physical slide duration — this lets Combatant treat its own
        // 0-1 slide progress as the clip's real normalized time when timing the ground-contact arc.
        // Bumped up twice now (0.8 -> 1 -> 1.4) to slow the whole knockback animation down further.
        public const float KnockbackDuration = 1.4f;

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
            controller.AddParameter("Guard", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("GuardIndex", AnimatorControllerParameterType.Float);
            controller.AddParameter("Death", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("DeathIndex", AnimatorControllerParameterType.Float);
            controller.AddParameter("LiteRelease", AnimatorControllerParameterType.Trigger);

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
            AnimationClip dodgeRightGuard = LoadClip(FightClipsFolder, "dodgeright");
            AnimationClip dodgeLeftGuard = LoadClip(FightClipsFolder, "dodgeleft");
            AnimationClip block1 = LoadClip(FightClipsFolder, "block");
            AnimationClip block2 = LoadClip(FightClipsFolder, "block2");
            AnimationClip death1 = LoadClip(FightClipsFolder, "Death");
            AnimationClip death2 = LoadClip(FightClipsFolder, "Death2");
            AnimationClip death3 = LoadClip(FightClipsFolder, "Death3");
            AnimationClip liteRelease = LoadClip(LiteClipsFolder, "Lite Release");

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
            knockbackState.speed = (knockback != null && knockback.length > 0f) ? knockback.length / KnockbackDuration : 1f;

            AnimatorStateTransition toKnockback = stateMachine.AddAnyStateTransition(knockbackState);
            toKnockback.hasExitTime = false;
            toKnockback.duration = 0.05f;
            toKnockback.canTransitionToSelf = false;
            toKnockback.AddCondition(AnimatorConditionMode.If, 0, "Knockback");

            // Exit as late as possible (unlike the other states' 0.9) — Knockback's state speed is
            // synced so its full length equals Combatant's knockbackDuration, so exitTime=0.9 would
            // start crossfading into Locomotion (blending in whatever pose movement produces at
            // that instant) *before* the slide coroutine's own control window ends at 1.0, making
            // hip height inconsistent run-to-run right when our correction curve needs it steady.
            AnimatorStateTransition knockbackToLocomotion = knockbackState.AddTransition(locomotionState);
            knockbackToLocomotion.hasExitTime = true;
            knockbackToLocomotion.exitTime = 0.98f;
            knockbackToLocomotion.duration = 0.1f;

            BlendTree guardTree = new BlendTree
            {
                name = "Guard Blend",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "GuardIndex",
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(guardTree, controller);

            if (dodgeRightGuard != null) guardTree.AddChild(dodgeRightGuard, 0f);
            if (dodgeLeftGuard != null) guardTree.AddChild(dodgeLeftGuard, 1f);
            if (block1 != null) guardTree.AddChild(block1, 2f);
            if (block2 != null) guardTree.AddChild(block2, 3f);

            AnimatorState guardState = stateMachine.AddState("Guard");
            guardState.motion = guardTree;

            AnimatorStateTransition toGuard = stateMachine.AddAnyStateTransition(guardState);
            toGuard.hasExitTime = false;
            toGuard.duration = 0.05f;
            toGuard.canTransitionToSelf = false;
            toGuard.AddCondition(AnimatorConditionMode.If, 0, "Guard");

            AnimatorStateTransition guardToLocomotion = guardState.AddTransition(locomotionState);
            guardToLocomotion.hasExitTime = true;
            guardToLocomotion.exitTime = 0.9f;
            guardToLocomotion.duration = 0.1f;

            // Attack's transition only originates from Locomotion (unlike Hit/Knockback/Guard,
            // which use AnyState), so without this, an attack fired right as Guard ends would
            // have to wait out Guard's own exit-time transition first, lagging behind the
            // already-ticking attack-cooldown/impact-delay timers. Let it cut in immediately.
            AnimatorStateTransition guardToAttack = guardState.AddTransition(attackState);
            guardToAttack.hasExitTime = false;
            guardToAttack.duration = 0.05f;
            guardToAttack.AddCondition(AnimatorConditionMode.If, 0, "Attack");

            BlendTree deathTree = new BlendTree
            {
                name = "Death Blend",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "DeathIndex",
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(deathTree, controller);

            if (death1 != null) deathTree.AddChild(death1, 0f);
            if (death2 != null) deathTree.AddChild(death2, 1f);
            if (death3 != null) deathTree.AddChild(death3, 2f);

            AnimatorState deathState = stateMachine.AddState("Death");
            deathState.motion = deathTree;

            // No outgoing transition at all — once dead, the clip (non-looping, per
            // CharacterModelPostprocessor's FightAnimations handling) just holds its last frame,
            // and EnemyDeath disables the Animator afterward to lock it in permanently.
            AnimatorStateTransition toDeath = stateMachine.AddAnyStateTransition(deathState);
            toDeath.hasExitTime = false;
            toDeath.duration = 0.1f;
            toDeath.canTransitionToSelf = false;
            toDeath.AddCondition(AnimatorConditionMode.If, 0, "Death");

            AnimatorState liteReleaseState = stateMachine.AddState("Lite Release");
            liteReleaseState.motion = liteRelease;

            AnimatorStateTransition toLiteRelease = stateMachine.AddAnyStateTransition(liteReleaseState);
            toLiteRelease.hasExitTime = false;
            toLiteRelease.duration = 0.05f;
            toLiteRelease.canTransitionToSelf = false;
            toLiteRelease.AddCondition(AnimatorConditionMode.If, 0, "LiteRelease");

            // Uses AnyState (like Hit/Knockback/Guard) rather than only transitioning out of
            // Locomotion — LiteReleaseAbility can be triggered from the hotbar regardless of what
            // the player is currently doing, and a real Hit/Knockback still preempts this state
            // the instant it fires since those also use AnyState transitions of their own.
            AnimatorStateTransition liteReleaseToLocomotion = liteReleaseState.AddTransition(locomotionState);
            liteReleaseToLocomotion.hasExitTime = true;
            liteReleaseToLocomotion.exitTime = 0.92f;
            liteReleaseToLocomotion.duration = 0.1f;

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
