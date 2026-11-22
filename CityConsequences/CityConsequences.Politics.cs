using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;
using ColossalFramework.UI;
using HarmonyLib;
using UnityEngine;

namespace CityConsequences
{
    static class Politics
    {
        internal const DistrictPolicies.Policies RentControl = (DistrictPolicies.Policies)32000, Subsidies = (DistrictPolicies.Policies)32001, Criminalize = (DistrictPolicies.Policies)32002, WorshipTax = (DistrictPolicies.Policies)32003, Outreach = (DistrictPolicies.Policies)32004, Interfaith = (DistrictPolicies.Policies)32005, PublicAll = (DistrictPolicies.Policies)32006, NoShortStay = (DistrictPolicies.Policies)32007;
        const int Pollution = 6, LowLandValue = 7, NoiseOrTrash = 8, HighRent = 20, NearbyHomeless = 21, FarFromWorship = 22, WorshipTaxed = 23, Agenda = 3;
        const float PollutionStart = 64f, PollutionRange = 128f, PollutionMax = 0.45f, NoiseStart = 64f, NoiseRange = 128f, NoiseMax = 0.35f, TrashWeight = 0.2f, NoiseTrashMax = 0.45f;
        const float LandShare = 0.6f, LandMax = 0.35f, RentStart = 0.35f, RentRange = 0.25f, RentMax = 0.7f, NoticeWeight = 0.8f, HomelessStart = 0.5f, HomelessBase = 0.15f, HomelessStep = 0.1f, HomelessMax = 0.5f;

        static bool applied;
        static Type grievanceType, pairType;
        static PropertyInfo pairKey, pairValue;
        const float SwingStart = 0.1f, SwingScale = 0.8f, SwingMax = 0.5f, DevoutSwing = 0.15f;
        static PropertyInfo language;
        static FieldInfo parties, supported, opposed, modifiers, taxRes, police, health, beauty, state, coalition, support;
        static readonly System.Random random = new System.Random();
        static readonly List<KeyValuePair<object, DistrictPolicies.Policies[]>> stash = new List<KeyValuePair<object, DistrictPolicies.Policies[]>>();
        [ThreadStatic] static List<KeyValuePair<object, float>> agenda;
        [ThreadStatic] static uint voter;
        static readonly Dictionary<int, object> keys = new Dictionary<int, object>();

        internal static bool Present { get; private set; }

        internal static void Reset() => applied = Present = false;

        internal static void Start()
        {
            if (applied) return;
            applied = true;
            try
            {
                Type engine = AccessTools.TypeByName("PoliticsMod.ElectionEngine"), grievances = AccessTools.TypeByName("PoliticsMod.Grievances"), editor = AccessTools.TypeByName("PoliticsMod.PartyEditorPanel");
                Type config = AccessTools.TypeByName("PoliticsMod.Config"), party = AccessTools.TypeByName("PoliticsMod.PartyDef"), mods = AccessTools.TypeByName("PoliticsMod.PolicyModifiers");
                grievanceType = AccessTools.TypeByName("PoliticsMod.Grievance");
                if (engine == null || grievances == null || config == null || party == null || mods == null || grievanceType == null) return;
                pairType = typeof(KeyValuePair<,>).MakeGenericType(grievanceType, typeof(float));
                pairKey = pairType.GetProperty("Key");
                pairValue = pairType.GetProperty("Value");
                parties = AccessTools.Field(config, "Parties");
                supported = AccessTools.Field(party, "VanillaPolicies");
                opposed = AccessTools.Field(party, "OpposedPolicies");
                modifiers = AccessTools.Field(party, "Modifiers");
                taxRes = AccessTools.Field(mods, "TaxDeltaRes");
                police = AccessTools.Field(mods, "BudgetDeltaPolice");
                health = AccessTools.Field(mods, "BudgetDeltaHealth");
                beauty = AccessTools.Field(mods, "BudgetDeltaBeautification");
                Type l10n = AccessTools.TypeByName("PoliticsMod.Localization.L10n");
                if (l10n != null) language = AccessTools.Property(l10n, "CurrentCode");
                Type politicsState = AccessTools.TypeByName("PoliticsMod.PoliticsState");
                if (politicsState != null)
                {
                    state = AccessTools.Field(politicsState, "Instance");
                    coalition = AccessTools.Field(politicsState, "CoalitionPartyIds");
                    support = AccessTools.Field(politicsState, "CurrentSupport");
                }
                var h = new Harmony(Patches.Id);
                Type self = typeof(Politics);
                Patches.Patch(h, self, engine, "DecideVote", null, nameof(Vote), nameof(Swing), Priority.First);
                if (Religion.Enabled) Patches.Patch(h, self, engine, "DecideVote", null, null, nameof(Devout));
                Patches.Patch(h, self, grievances, "Compute", null, null, nameof(Compute));
                Patches.Patch(h, self, grievances, "ScorePartyForGrievance", null, null, nameof(Score));
                Patches.Patch(h, self, engine, "ApplyCoalitionPolicies", null, nameof(Filter), null, Priority.First, nameof(Restore));
                Patches.Patch(h, self, engine, "FormatPolicyTitle", null, nameof(Title), null);
                Patches.Patch(h, self, editor, "GetInterestingPolicies", null, null, nameof(Interesting));
                Patches.Patch(h, self, editor, "FormatPolicyName", null, nameof(Name), null);
                Patches.Patch(h, self, editor, "BuildPolicyTile", null, null, nameof(Tile));
                Patches.Patch(h, self, typeof(DistrictManager), "SetCityPolicy", new[] { typeof(DistrictPolicies.Policies) }, nameof(Set), null, Priority.First);
                Patches.Patch(h, self, typeof(DistrictManager), "UnsetCityPolicy", new[] { typeof(DistrictPolicies.Policies) }, nameof(Unset), null, Priority.First);
                Patches.Patch(h, self, typeof(DistrictManager), "IsCityPolicySet", new[] { typeof(DistrictPolicies.Policies) }, nameof(IsSet), null, Priority.First);
                Patches.Patch(h, self, typeof(District), "IsPolicySet", new[] { typeof(DistrictPolicies.Policies) }, nameof(IsSet), null, Priority.First);
                Present = true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        static byte Bit(DistrictPolicies.Policies p)
        {
            switch ((int)p)
            {
                case 32000: return Housing.ControlBit;
                case 32001: return Housing.SubsidyBit;
                case 32002: return Housing.CriminalBit;
                case 32003: return Religion.TaxBit;
                case 32004: return Religion.OutreachBit;
                case 32005: return Religion.InterfaithBit;
                case 32006: return Housing.PublicBit;
                case 32007: return Housing.BanBit;
                default: return 0;
            }
        }

        static bool Ours(DistrictPolicies.Policies p) => (int)p >= 32000 && (int)p <= 32007;
        static bool Faith(DistrictPolicies.Policies p) => p == WorshipTax || p == Outreach || p == Interfaith;

        static string Key(DistrictPolicies.Policies p)
        {
            switch ((int)p)
            {
                case 32000: return "pol_control";
                case 32001: return "pol_subsidy";
                case 32002: return "pol_criminal";
                case 32003: return "pol_worship_tax";
                case 32004: return "pol_outreach";
                case 32006: return "pol_public_all";
                case 32007: return "pol_no_str";
                default: return "pol_interfaith";
            }
        }

        static bool Unlocked(DistrictPolicies.Policies p)
        {
            if (p == Criminalize) return Settings.Homeless.value && Singleton<UnlockManager>.instance.Unlocked(ItemClass.Service.PoliceDepartment);
            if (p == WorshipTax) return Religion.Enabled && ToolsModifierControl.IsUnlocked(DistrictPolicies.Types.Taxation);
            if (Faith(p)) return Religion.Enabled && ToolsModifierControl.IsUnlocked(DistrictPolicies.Types.Services);
            if (Ours(p)) return ToolsModifierControl.IsUnlocked(DistrictPolicies.Types.Taxation);
            DistrictManager dm = Singleton<DistrictManager>.instance;
            return dm.IsPolicyLoaded(p) && ToolsModifierControl.IsUnlocked(p);
        }

        static bool Vote(object[] __args, ref int __result)
        {
            voter = 0;
            if (!Sim.Active || !(__args[0] is Citizen c)) return true;
            if (c.m_homeBuilding != 0 && (Religion.On || Settings.Rent.value && Housing.IsLow(c.m_homeBuilding))) voter = Religion.Identify(ref c);
            if (!Settings.Homeless.value || !Housing.Voteless(c.m_homeBuilding)) return true;
            __result = -1;
            return false;
        }

        static List<int> Coalition(out object st)
        {
            st = state != null ? state.GetValue(null) : null;
            return st != null && coalition != null ? coalition.GetValue(st) as List<int> : null;
        }

        internal static int Government()
        {
            if (!Present) return 0;
            try
            {
                List<int> ids = Coalition(out object _);
                if (ids == null || ids.Count == 0) return 0;
                var sorted = new List<int>(ids);
                sorted.Sort();
                int key = 17;
                foreach (int id in sorted) key = key * 31 + id + 1;
                return key == 0 ? 1 : key;
            }
            catch { return 0; }
        }

        static void Swing(object[] __args, ref int __result)
        {
            if (!Sim.Active || __result < 0 || !(__args[0] is Citizen c) || c.m_homeBuilding == 0) return;
            try
            {
                float m = Community.Mobilization(Singleton<BuildingManager>.instance.m_buildings.m_buffer[c.m_homeBuilding].m_position);
                if (m <= SwingStart) return;
                List<int> ids = Coalition(out object st);
                if (ids == null || ids.Count == 0 || !ids.Contains(__result)) return;
                lock (random) if (random.NextDouble() >= Math.Min(SwingMax, m * SwingScale)) return;
                float[] shares = support != null ? support.GetValue(st) as float[] : null;
                int count = parties.GetValue(null) is Array all ? all.Length : 0, best = -1;
                float bestShare = -1f;
                for (int i = 0; i < count; i++)
                {
                    if (ids.Contains(i)) continue;
                    float share = shares != null && i < shares.Length ? shares[i] : 0f;
                    if (share > bestShare) { bestShare = share; best = i; }
                }
                if (best >= 0) __result = best;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        static float Devoted(object party) => -Stance(party, WorshipTax) + 0.5f * Stance(party, Outreach) + 0.3f * Stance(party, Interfaith);

        static void Devout(ref int __result)
        {
            uint id = voter;
            voter = 0;
            if (__result < 0 || id == 0 || !Religion.On) return;
            try
            {
                float devotion = Religion.Devotion(id);
                if (devotion < 1f) return;
                lock (random) if (random.NextDouble() >= DevoutSwing * devotion) return;
                if (!(parties.GetValue(null) is Array all) || __result >= all.Length) return;
                float best = Devoted(all.GetValue(__result));
                int pick = __result;
                for (int i = 0; i < all.Length; i++)
                {
                    object party = all.GetValue(i);
                    if (party == null) continue;
                    float f = Devoted(party);
                    if (f > best + 0.25f) { best = f; pick = i; }
                }
                __result = pick;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        static void Compute(object[] __args, object __result)
        {
            if (!Sim.Active || !(__result is IList list) || !(__args[0] is Citizen c) || c.m_homeBuilding == 0) return;
            try
            {
                Building[] b = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
                ushort home = c.m_homeBuilding;
                Vector3 p = b[home].m_position;
                if (agenda == null) agenda = new List<KeyValuePair<object, float>>(8);
                agenda.Clear();
                foreach (object entry in list) agenda.Add(new KeyValuePair<object, float>(entry, (float)pairValue.GetValue(entry, null)));
                Singleton<NaturalResourceManager>.instance.CheckPollution(p, out byte ground);
                Add(Pollution, PollutionMax * Mathf.Clamp01((ground - PollutionStart) / PollutionRange));
                Singleton<ImmaterialResourceManager>.instance.CheckLocalResource(ImmaterialResourceManager.Resource.NoisePollution, p, out int noise);
                float trash = (b[home].m_problems & Notification.Problem1.Garbage).IsNotNone ? TrashWeight : 0f;
                Add(NoiseOrTrash, Mathf.Min(NoiseTrashMax, NoiseMax * Mathf.Clamp01((noise - NoiseStart) / NoiseRange) + trash));
                int average = Singleton<DistrictManager>.instance.m_districts.m_buffer[0].GetLandValue();
                Singleton<ImmaterialResourceManager>.instance.CheckLocalResource(ImmaterialResourceManager.Resource.LandValue, p, out int land);
                if (average > 10) Add(LowLandValue, LandMax * Mathf.Clamp01(1f - land / (LandShare * average)));
                if (Settings.Rent.value)
                {
                    byte burden = Housing.Burden(home);
                    Add(HighRent, burden == Housing.NoticeBurden ? NoticeWeight : RentMax * Mathf.Clamp01((burden / 100f - RentStart) / RentRange));
                }
                if (Settings.Homeless.value)
                {
                    float near = Housing.PresenceAt(p);
                    if (near > HomelessStart) Add(NearbyHomeless, Mathf.Min(HomelessMax, HomelessBase + HomelessStep * near));
                }
                if (voter != 0 && Settings.Rent.value)
                {
                    float loss = Housing.OwnerLoss(voter, home);
                    if (loss > 0f) Add(LowLandValue, loss);
                }
                if (voter != 0 && Religion.On && Religion.Grievances(voter, home, out float far, out float taxed))
                {
                    Add(FarFromWorship, far);
                    Add(WorshipTaxed, taxed);
                }
                agenda.Sort((x, y) => y.Value.CompareTo(x.Value));
                list.Clear();
                for (int i = 0; i < agenda.Count && i < Agenda; i++) list.Add(agenda[i].Key);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        static void Add(int grievance, float weight)
        {
            if (weight <= 0.01f) return;
            object key;
            lock (keys) if (!keys.TryGetValue(grievance, out key)) keys[grievance] = key = Enum.ToObject(grievanceType, grievance);
            foreach (var a in agenda) if (Equals(pairKey.GetValue(a.Key, null), key)) return;
            agenda.Add(new KeyValuePair<object, float>(Activator.CreateInstance(pairType, key, weight), weight));
        }

        static int Stance(object party, DistrictPolicies.Policies p)
        {
            if (supported.GetValue(party) is DistrictPolicies.Policies[] s && Array.IndexOf(s, p) >= 0) return 1;
            if (opposed.GetValue(party) is DistrictPolicies.Policies[] o && Array.IndexOf(o, p) >= 0) return -1;
            return 0;
        }

        static float Modifier(object party, FieldInfo f)
        {
            object m = modifiers.GetValue(party);
            return m != null ? Convert.ToSingle(f.GetValue(m)) : 0f;
        }

        static void Score(object[] __args, ref float __result)
        {
            object party = __args[0];
            if (party == null) return;
            int g = Convert.ToInt32(__args[1]);
            if (g == FarFromWorship || g == WorshipTaxed)
            {
                int tax = Stance(party, WorshipTax), help = Stance(party, Outreach), inter = Stance(party, Interfaith);
                float f = g == FarFromWorship
                    ? 0.6f * inter + 0.25f * help - 0.2f * tax + (beauty != null ? Modifier(party, beauty) / 40f : 0f)
                    : -0.8f * tax + 0.2f * help - Modifier(party, taxRes) / 16f;
                __result = Mathf.Clamp(f, -1f, 1f);
                return;
            }
            if (g != HighRent && g != NearbyHomeless) return;
            int control = Stance(party, RentControl), subsidy = Stance(party, Subsidies), crime = Stance(party, Criminalize), everyone = Stance(party, PublicAll), ban = Stance(party, NoShortStay);
            float s = g == HighRent
                ? 0.6f * control + 0.5f * subsidy + 0.7f * everyone + 0.4f * ban - Modifier(party, taxRes) / 8f
                : 0.25f * control + 0.35f * subsidy + 0.45f * crime + 0.4f * everyone + Modifier(party, police) / 30f + Modifier(party, health) / 40f;
            __result = Mathf.Clamp(s, -1f, 1f);
        }

        static void Filter()
        {
            stash.Clear();
            if (!(parties.GetValue(null) is Array all)) return;
            foreach (object party in all)
            {
                if (party == null || !(supported.GetValue(party) is DistrictPolicies.Policies[] list)) continue;
                var keep = new List<DistrictPolicies.Policies>(list.Length);
                foreach (var p in list) if (Unlocked(p)) keep.Add(p);
                if (keep.Count == list.Length) continue;
                stash.Add(new KeyValuePair<object, DistrictPolicies.Policies[]>(party, list));
                supported.SetValue(party, keep.ToArray());
            }
        }

        static void Restore()
        {
            foreach (var s in stash) supported.SetValue(s.Key, s.Value);
            stash.Clear();
        }

        static string Language()
        {
            try { return language != null ? language.GetValue(null, null) as string ?? "en" : "en"; }
            catch { return "en"; }
        }

        static bool Title(DistrictPolicies.Policies policy, ref string __result)
        {
            if (!Ours(policy)) return true;
            __result = Lang.T(Key(policy) + "_bill", Language());
            return false;
        }

        static bool Name(DistrictPolicies.Policies p, ref string __result)
        {
            if (!Ours(p)) return true;
            __result = Lang.T(Key(p), Language());
            return false;
        }

        static void Interesting(ref DistrictPolicies.Policies[] __result)
        {
            if (__result == null || Array.IndexOf(__result, RentControl) >= 0) return;
            var list = new List<DistrictPolicies.Policies>(__result) { RentControl, Subsidies, PublicAll, NoShortStay };
            if (Settings.Homeless.value) list.Add(Criminalize);
            if (Religion.Enabled) { list.Add(WorshipTax); list.Add(Outreach); list.Add(Interfaith); }
            __result = list.ToArray();
        }

        static void Tile(UIButton __result, DistrictPolicies.Policies policy)
        {
            if (__result == null || !Ours(policy)) return;
            foreach (UIComponent child in __result.components)
            {
                if (!(child is UISprite icon)) continue;
                if (policy == RentControl || policy == Subsidies)
                {
                    icon.spriteName = policy == RentControl ? "IconPolicyTaxLowerResHigh" : "IconPolicyTaxLowerResLow";
                    continue;
                }
                if (Icons.Atlas == null) continue;
                icon.atlas = Icons.Atlas;
                icon.spriteName = policy == Criminalize ? Icons.AntiHomeless : policy == PublicAll ? Icons.PublicHousing : policy == NoShortStay ? Icons.NoShortStay : policy == WorshipTax ? Icons.WorshipTax : policy == Outreach ? Icons.Outreach : Icons.InterfaithPolicy;
            }
        }

        static bool Set(DistrictPolicies.Policies policy)
        {
            if (!Ours(policy)) return true;
            Housing.SetPolicy(0, Bit(policy), true);
            return false;
        }

        static bool Unset(DistrictPolicies.Policies policy)
        {
            if (!Ours(policy)) return true;
            Housing.SetPolicy(0, Bit(policy), false);
            return false;
        }

        static bool IsSet(DistrictPolicies.Policies policy, ref bool __result)
        {
            if (!Ours(policy)) return true;
            __result = Housing.HasPolicy(0, Bit(policy));
            return false;
        }
    }
}
