using System;
using System.IO;
using LLMUnity;
using UnityEditor;
using UnityEngine;

namespace Darclite.EditorTools
{
    // Phase 1 of the NPC dialogue system: get a single shared local model downloaded and hosted
    // so an LLMAgent on an NPC has something to talk to. Small/fast model to start — swap for a
    // bigger one later once the wiring itself is proven out.
    public static class LLMSetupTools
    {
        private const string StarterModelUrl = "https://huggingface.co/hugging-quants/Llama-3.2-3B-Instruct-Q4_K_M-GGUF/resolve/main/llama-3.2-3b-instruct-q4_k_m.gguf";
        private const string StarterModelLabel = "Llama 3.2 3B Instruct";

        [MenuItem("Darclite/LLM/Setup LLM Host (Download Model)")]
        public static async void SetupLLMHost()
        {
            try
            {
                Debug.Log("[LLMSetupTools] Downloading starter model (a couple GB the first time) — this can take a while depending on your connection...");
                string modelFilename = await LLMManager.DownloadModel(StarterModelUrl, log: true, label: StarterModelLabel);

                if (string.IsNullOrEmpty(modelFilename))
                {
                    Debug.LogError("[LLMSetupTools] Model download failed — see the error above.");
                    return;
                }

                GameObject hostObject = GameObject.Find("LLMHost");
                if (hostObject == null)
                {
                    hostObject = new GameObject("LLMHost");
                    Undo.RegisterCreatedObjectUndo(hostObject, "Create LLM Host");
                }

                LLM llm = hostObject.GetComponent<LLM>();
                if (llm == null)
                {
                    llm = hostObject.AddComponent<LLM>();
                }

                llm.model = modelFilename;

                // Only one NPC conversation is ever active at once in this single-player game, so
                // a fixed small slot count is more memory-efficient than auto-detecting from
                // however many NPCs register as clients as the roster of talking NPCs grows.
                llm.parallelPrompts = 1;

                Selection.activeGameObject = hostObject;
                Debug.Log($"[LLMSetupTools] LLM Host ready with model '{modelFilename}'. Run 'Darclite/Setup Quest NPC' next to give her an LLMAgent.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        // Every NPC's chat history persists to disk automatically (LLMAgent's Save field), which
        // is exactly what you want in a real playthrough but gets in the way while iterating on a
        // persona — without this, every playtest keeps piling onto the same conversation. Wipes
        // every saved NPC conversation so the next chat starts fresh.
        [MenuItem("Darclite/LLM/Clear All NPC Chat Memory")]
        public static void ClearAllNpcChatMemory()
        {
            string folder = Path.Combine(Application.persistentDataPath, SceneBootstrapper.NPCChatSaveFolder);
            if (!Directory.Exists(folder))
            {
                Debug.Log("[LLMSetupTools] No saved NPC chat memory found — nothing to clear.");
                return;
            }

            string[] files = Directory.GetFiles(folder, "*.json");
            foreach (string file in files)
            {
                File.Delete(file);
            }

            Debug.Log($"[LLMSetupTools] Cleared {files.Length} saved NPC conversation(s) from '{folder}'.");
        }
    }
}
