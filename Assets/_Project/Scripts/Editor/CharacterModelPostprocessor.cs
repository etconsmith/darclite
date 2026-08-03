using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Darclite.EditorTools
{
    public class CharacterModelPostprocessor : AssetPostprocessor
    {
        private const string CharactersFolder = "Assets/_Project/Art/Characters";
        private const string MixamoAnimationsFolder = "Assets/_Project/Animations/Mixamo";
        private const string FightAnimationsFolder = "Assets/_Project/Animations/FightAnimations";
        private const string LiteAnimationsFolder = "Assets/_Project/Animations/Lite Animations";

        private static readonly (string boneName, string humanName)[] BoneMap =
        {
            // "Body" is the real pelvis/common ancestor of both the spine and the legs across
            // every model in this pack (Warrior/Monk/Rogue/Cleric/Ranger/Wizard) — the bone
            // literally named "Hips" is actually just the first spine segment and sits as a
            // *sibling* of UpperLeg.L/R, not their ancestor. Mapping human Hips to the literal
            // "Hips" bone fails Unity's Avatar requirement that Hips be an ancestor of UpperLeg.
            ("Body", "Hips"),
            ("Abdomen", "Spine"),
            ("Torso", "Chest"),
            ("Neck", "Neck"),
            ("Head", "Head"),
            ("Shoulder.L", "LeftShoulder"),
            ("UpperArm.L", "LeftUpperArm"),
            ("LowerArm.L", "LeftLowerArm"),
            ("Fist.L", "LeftHand"),
            ("Shoulder.R", "RightShoulder"),
            ("UpperArm.R", "RightUpperArm"),
            ("LowerArm.R", "RightLowerArm"),
            ("Fist.R", "RightHand"),
            ("UpperLeg.L", "LeftUpperLeg"),
            ("LowerLeg.L", "LeftLowerLeg"),
            ("Foot.L", "LeftFoot"),
            ("UpperLeg.R", "RightUpperLeg"),
            ("LowerLeg.R", "RightLowerLeg"),
            ("Foot.R", "RightFoot"),
        };

        private bool IsCharacterAsset()
        {
            string path = assetPath.Replace('\\', '/');
            return path.StartsWith(CharactersFolder) && path.Contains("/Models/");
        }

        private bool IsMixamoAnimationAsset()
        {
            return assetPath.Replace('\\', '/').StartsWith(MixamoAnimationsFolder);
        }

        private bool IsFightAnimationAsset()
        {
            return assetPath.Replace('\\', '/').StartsWith(FightAnimationsFolder);
        }

        private bool IsLiteAnimationAsset()
        {
            return assetPath.Replace('\\', '/').StartsWith(LiteAnimationsFolder);
        }

        private void OnPreprocessModel()
        {
            ModelImporter importer = (ModelImporter)assetImporter;

            if (IsCharacterAsset())
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.importAnimation = true;
                importer.importNormals = ModelImporterNormals.Calculate;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                importer.optimizeGameObjects = false;
                return;
            }

            if (IsMixamoAnimationAsset() || IsFightAnimationAsset() || IsLiteAnimationAsset())
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.importAnimation = true;

                string fileName = Path.GetFileNameWithoutExtension(assetPath);
                string lowerFileName = fileName.ToLowerInvariant();
                bool shouldLoop = IsMixamoAnimationAsset() && !lowerFileName.Contains("jump") && !lowerFileName.Contains("dodge");

                TakeInfo[] takes = importer.importedTakeInfos;
                if (takes != null && takes.Length > 0)
                {
                    ModelImporterClipAnimation[] clips = new ModelImporterClipAnimation[takes.Length];
                    for (int i = 0; i < takes.Length; i++)
                    {
                        clips[i] = new ModelImporterClipAnimation
                        {
                            name = fileName,
                            takeName = takes[i].name,
                            firstFrame = takes[i].bakeStartTime * takes[i].sampleRate,
                            lastFrame = takes[i].bakeStopTime * takes[i].sampleRate,
                            wrapMode = shouldLoop ? WrapMode.Loop : WrapMode.Once,
                            loopTime = shouldLoop,
                            loopPose = shouldLoop
                        };
                    }

                    importer.clipAnimations = clips;
                }
            }
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!IsCharacterAsset())
            {
                return;
            }

            HumanBone[] humanBones = BuildHumanBones(root.transform);
            if (humanBones == null)
            {
                return;
            }

            SkeletonBone[] skeletonBones = BuildSkeletonBones(root.transform);

            HumanDescription description = new HumanDescription
            {
                human = humanBones,
                skeleton = skeletonBones,
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f,
                hasTranslationDoF = false,
            };

            ModelImporter importer = (ModelImporter)assetImporter;
            importer.humanDescription = description;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        }

        private static HumanBone[] BuildHumanBones(Transform root)
        {
            var bones = new List<HumanBone>();

            foreach ((string boneName, string humanName) in BoneMap)
            {
                Transform found = FindDescendant(root, boneName);
                if (found == null)
                {
                    Debug.LogWarning($"[CharacterModelPostprocessor] Could not find bone '{boneName}' on '{root.name}'; skipping humanoid mapping for this model.");
                    return null;
                }

                bones.Add(new HumanBone
                {
                    boneName = boneName,
                    humanName = humanName,
                    limit = new HumanLimit { useDefaultValues = true }
                });
            }

            return bones.ToArray();
        }

        private static SkeletonBone[] BuildSkeletonBones(Transform root)
        {
            var bones = new List<SkeletonBone>();
            var seenNames = new HashSet<string>();
            CollectSkeleton(root, bones, seenNames);
            return bones.ToArray();
        }

        private static void CollectSkeleton(Transform t, List<SkeletonBone> bones, HashSet<string> seenNames)
        {
            // Some rigs (e.g. Cleric) have a genuine duplicate bone name deeper in the hierarchy.
            // Only the first occurrence is kept, matching FindDescendant's resolution order, so
            // the Avatar system can't ambiguously resolve a mapped human bone to the wrong transform.
            if (seenNames.Add(t.name))
            {
                bones.Add(new SkeletonBone
                {
                    name = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale
                });
            }

            foreach (Transform child in t)
            {
                CollectSkeleton(child, bones, seenNames);
            }
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            foreach (Transform child in root)
            {
                Transform result = FindDescendant(child, name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}
