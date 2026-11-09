using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Snaploop.Monetization
{
    /// <summary>
    /// Locally persisted ownership of non-consumables (Remove Ads and theme packs). Stored in its own file under
    /// <c>Application.persistentDataPath</c> rather than PlayerPrefs, so the dev menu's
    /// "Reset progress" (PlayerPrefs.DeleteAll) does not take away a paid purchase.
    /// Google Play stays the source of truth: purchases are re-read from the store on every launch.
    /// </summary>
    public static class Entitlements
    {
        [Serializable]
        sealed class Data
        {
            public bool removeAds;
            public List<string> products = new List<string>();
        }

        const string FileName = "entitlements.json";
        static Data data;

        public static event Action Changed;

        public static bool RemoveAds
        {
            get => Load().removeAds;
            set
            {
                if (Load().removeAds == value) return;
                data.removeAds = value;
                Save();
                Changed?.Invoke();
            }
        }

        /// <summary>Other non-consumables (theme packs), by store product id.</summary>
        public static bool Owns(string productId) => !string.IsNullOrEmpty(productId) && Load().products.Contains(productId);

        public static void Set(string productId, bool owned)
        {
            if (string.IsNullOrEmpty(productId) || Owns(productId) == owned) return;
            if (owned) data.products.Add(productId);
            else data.products.Remove(productId);
            Save();
            Changed?.Invoke();
        }

        public static int OwnedProductCount => Load().products.Count;

        /// <summary>Dev menu: forget every product except Remove Ads.</summary>
        public static void ForgetProducts()
        {
            if (Load().products.Count == 0) return;
            data.products.Clear();
            Save();
            Changed?.Invoke();
        }

        static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        static Data Load()
        {
            if (data != null) return data;
            data = new Data();
            try
            {
                if (File.Exists(FilePath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), data);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Monetization] Could not read " + FileName + ": " + e.Message);
            }
            if (data.products == null) data.products = new List<string>();
            return data;
        }

        static void Save()
        {
            try
            {
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(data));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception e)
            {
                Debug.LogError("[Monetization] Could not save " + FileName + ": " + e.Message);
            }
        }
    }
}
