using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CitiesHarmony.API;
using ColossalFramework;
using ColossalFramework.Globalization;
using ColossalFramework.Math;
using ColossalFramework.Threading;
using ColossalFramework.UI;
using HarmonyLib;
using ICities;
using UnityEngine;

namespace CityConsequences
{
    public class Mod : IUserMod
    {
        public string Name => "City Consequences";
        public string Description => Lang.T("desc");

        public void OnEnabled()
        {
            Settings.Init();
            HarmonyHelper.DoOnHarmonyReady(Patches.Apply);
        }

        public void OnDisabled()
        {
            if (HarmonyHelper.IsHarmonyInstalled) Patches.Remove();
        }

        public void OnSettingsUI(UIHelperBase helper)
        {
            Settings.Init();
            UIHelperBase g = helper.AddGroup(Name);
            Toggle(g, "opt_rent", Settings.Rent);
            Toggle(g, "opt_homeless", Settings.Homeless);
            Toggle(g, "opt_tents", Settings.Tents);
            Toggle(g, "opt_comp", Settings.Compensation);
            Toggle(g, "opt_roads", Settings.Roads);
            Toggle(g, "opt_walk", Settings.Walk);
            Toggle(g, "opt_payroll", Settings.Payroll);
            Toggle(g, "opt_ageing", Settings.Ageing);
            Slider(g, "opt_comp_scale", 0.25f, 4f, 0.25f, Settings.CompensationScale);
            Slider(g, "opt_road_scale", 0.25f, 4f, 0.25f, Settings.RoadScale);
            Slider(g, "opt_control", 0.1f, 0.9f, 0.05f, Settings.ControlStrength);
            Slider(g, "opt_subsidy", 0.1f, 0.9f, 0.05f, Settings.SubsidyShare);
            Slider(g, "opt_tent_cap", 0f, 2000f, 50f, Settings.TentCap);
            Slider(g, "opt_record_weeks", 0f, 52f, 1f, Settings.RecordWeeks);
            g.AddTextfield(Lang.T("opt_tent_prop"), Settings.TentProp.value, v => { }, v => { Settings.TentProp.value = v; if (Sim.Active) Tents.Reset(); });
            Slider(g, "opt_holiday_share", 0f, 50f, 5f, Settings.HolidayShare);
            Slider(g, "opt_holiday_attraction", 25f, 400f, 25f, Settings.HolidayAttraction);
            if (Religion.Standalone) return;
            Toggle(g, "opt_religion", Settings.Faith);
            g.AddDropdown(Lang.T("opt_birth"), new[] { Lang.T("opt_birth_mother"), Lang.T("opt_birth_both"), Lang.T("opt_birth_off") }, Mathf.Clamp(Settings.BirthMode.value, 0, 2), i => Settings.BirthMode.value = i);
            Slider(g, "opt_birth_strength", 0f, 200f, 10f, Settings.BirthStrength);
            UIButton clear = null;
            clear = g.AddButton(string.Format(Lang.T("opt_worship_assets"), Settings.WorshipCount), () =>
            {
                Settings.ClearWorship();
                if (clear != null) clear.text = string.Format(Lang.T("opt_worship_assets"), 0);
            }) as UIButton;
        }

        static void Toggle(UIHelperBase g, string id, SavedBool s) => g.AddCheckbox(Lang.T(id), s.value, v => s.value = v);
        static void Slider(UIHelperBase g, string id, float min, float max, float step, SavedFloat s) => g.AddSlider(Lang.T(id), min, max, step, s.value, v => s.value = v);
    }

    static class Settings
    {
        const string File = "CityConsequences";
        public static SavedBool Rent, Homeless, Tents, Compensation, Roads, Walk, Payroll, Ageing;
        public static SavedFloat CompensationScale, RoadScale, ControlStrength, SubsidyShare, TentCap, RecordWeeks;
        public static SavedString TentProp, WorshipAssets;
        public static SavedBool Faith;
        public static SavedInt BirthMode;
        public static SavedFloat BirthStrength, HolidayShare, HolidayAttraction;
        static HashSet<string> worship;
        static readonly object worshipLock = new object();

        public static void Init()
        {
            if (Rent != null) return;
            if (GameSettings.FindSettingsFileByName(File) == null) GameSettings.AddSettingsFile(new SettingsFile { fileName = File });
            Rent = new SavedBool("rent", File, true, true);
            Homeless = new SavedBool("homeless", File, true, true);
            Tents = new SavedBool("tents", File, true, true);
            Compensation = new SavedBool("compensation", File, true, true);
            Roads = new SavedBool("roads", File, true, true);
            Walk = new SavedBool("walk", File, true, true);
            Payroll = new SavedBool("payroll", File, true, true);
            Ageing = new SavedBool("ageing", File, true, true);
            CompensationScale = new SavedFloat("compensationScale", File, 1f, true);
            RoadScale = new SavedFloat("roadScale", File, 1f, true);
            ControlStrength = new SavedFloat("controlStrength", File, 0.6f, true);
            SubsidyShare = new SavedFloat("subsidyShare", File, 0.3f, true);
            TentProp = new SavedString("tentProp", File, "", true);
            TentCap = new SavedFloat("tentCap", File, 500f, true);
            RecordWeeks = new SavedFloat("recordWeeks", File, 12f, true);
            Faith = new SavedBool("religion", File, true, true);
            BirthMode = new SavedInt("birthMode", File, 0, true);
            BirthStrength = new SavedFloat("birthStrength", File, 100f, true);
            WorshipAssets = new SavedString("worshipAssets", File, "", true);
            HolidayShare = new SavedFloat("holidayShare", File, 25f, true);
            HolidayAttraction = new SavedFloat("holidayAttraction", File, 150f, true);
            if (WorshipAssets.value.Length == 0) Import();
        }

        static void Import()
        {
            try
            {
                string path = System.IO.Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "PlacesOfWorship.txt");
                if (!System.IO.File.Exists(path)) return;
                var found = new List<string>();
                foreach (string line in System.IO.File.ReadAllLines(path))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i), v = line.Substring(i + 1);
                    if (k == "asset" && v.Length > 0 && v.IndexOf('|') < 0) found.Add(v);
                    else if (k == "birth" && int.TryParse(v, out int b)) BirthMode.value = Mathf.Clamp(b, 0, 2);
                    else if (k == "strength" && int.TryParse(v, out int st)) BirthStrength.value = Mathf.Clamp(st, 0, 200);
                }
                if (found.Count > 0) WorshipAssets.value = string.Join("|", found.ToArray());
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        static HashSet<string> Worship()
        {
            if (worship != null) return worship;
            var set = new HashSet<string>();
            foreach (string n in WorshipAssets.value.Split('|')) if (n.Length > 0) set.Add(n);
            return worship = set;
        }

        internal static bool IsWorship(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            Init();
            lock (worshipLock) return Worship().Contains(name);
        }

        internal static int WorshipCount { get { Init(); lock (worshipLock) return Worship().Count; } }

        internal static void SetWorship(string name, bool on)
        {
            if (string.IsNullOrEmpty(name) || name.IndexOf('|') >= 0) return;
            lock (worshipLock)
            {
                HashSet<string> set = Worship();
                if (on ? !set.Add(name) : !set.Remove(name)) return;
                var all = new string[set.Count];
                set.CopyTo(all);
                WorshipAssets.value = string.Join("|", all);
            }
        }

        internal static void ClearWorship()
        {
            lock (worshipLock) { Worship().Clear(); WorshipAssets.value = ""; }
        }
    }

    static class Lang
    {
        static readonly string[] Codes = { "en", "de", "es", "fr", "ko", "pl", "pt", "ru", "zh" };

        static readonly Dictionary<string, string[]> Text = new Dictionary<string, string[]>
        {
            ["desc"] = new[] {
                "Rent, eviction, homelessness, displacement, road upkeep, walkable growth, housing policies, aging infrastructure and places of worship.",
                "Miete, Zwangsräumung, Obdachlosigkeit, Verdrängung, Straßenunterhalt, fußläufiges Wachstum, Wohnungspolitik, alternde Infrastruktur und Gotteshäuser.",
                "Alquiler, desahucios, personas sin hogar, desplazamiento, mantenimiento de carreteras, crecimiento caminable, políticas de vivienda, infraestructura envejecida y lugares de culto.",
                "Loyers, expulsions, sans-abrisme, déplacements forcés, entretien des routes, croissance piétonne, politiques du logement, infrastructures vieillissantes et lieux de culte.",
                "월세, 쫓겨남, 노숙, 강제 철거, 도로 유지비, 걷기 좋은 동네, 주거 정책, 낡아가는 인프라, 그리고 예배 장소까지.",
                "Czynsze, eksmisje, bezdomność, przesiedlenia, utrzymanie dróg, rozwój przyjazny pieszym, polityka mieszkaniowa, starzejąca się infrastruktura i miejsca kultu.",
                "Aluguel, despejos, pessoas em situação de rua, deslocamento, manutenção de vias, crescimento caminhável, políticas habitacionais, infraestrutura envelhecida e locais de culto.",
                "Аренда, выселения, бездомность, вынужденное переселение, содержание дорог, пешеходная доступность, жилищная политика, стареющая инфраструктура и места поклонения.",
                "租金、驱逐、无家可归、拆迁安置、道路维护、步行友好发展、住房政策、老化的基础设施与礼拜场所。" },
            ["opt_rent"] = new[] { "Rent and eviction", "Miete und Zwangsräumung", "Alquiler y desahucios", "Loyers et expulsions", "월세와 퇴거", "Czynsze i eksmisje", "Aluguel e despejos", "Аренда и выселения", "租金与驱逐" },
            ["opt_homeless"] = new[] { "Homelessness", "Obdachlosigkeit", "Personas sin hogar", "Sans-abrisme", "노숙", "Bezdomność", "Pessoas em situação de rua", "Бездомность", "无家可归" },
            ["opt_tents"] = new[] { "Tents for homeless households", "Zelte für obdachlose Haushalte", "Tiendas de campaña para hogares sin techo", "Tentes pour les foyers sans abri", "노숙 가구 텐트", "Namioty bezdomnych gospodarstw", "Barracas para famílias sem moradia", "Палатки бездомных семей", "无家可归家庭的帐篷" },
            ["opt_comp"] = new[] { "Compensation for demolished homes", "Entschädigung für abgerissene Wohnungen", "Indemnización por viviendas demolidas", "Indemnisation des logements démolis", "철거된 집 보상금", "Odszkodowania za wyburzone domy", "Indenização por moradias demolidas", "Компенсация за снесённое жильё", "拆除住房补偿" },
            ["opt_roads"] = new[] { "Higher road upkeep", "Höhere Straßenunterhaltskosten", "Mayor mantenimiento de carreteras", "Entretien des routes plus coûteux", "훨씬 비싸진 도로 유지비", "Wyższe koszty utrzymania dróg", "Manutenção de vias mais cara", "Повышенное содержание дорог", "更高的道路维护费" },
            ["opt_walk"] = new[] { "Walkability and transit limit upgrades", "Fußläufigkeit und ÖPNV begrenzen Aufwertungen", "La caminabilidad y el transporte limitan las mejoras", "La marchabilité et les transports limitent les améliorations", "걷기 좋고 대중교통이 편해야 건물이 업그레이드됨", "Dostępność pieszo i komunikacja ograniczają rozwój budynków", "Caminhabilidade e transporte limitam melhorias", "Пешая и транспортная доступность ограничивают улучшения", "步行与公交可达性限制建筑升级" },
            ["opt_payroll"] = new[] { "City service wages follow how rich the city is", "Löhne im öffentlichen Dienst folgen dem Wohlstand der Stadt", "Los sueldos de los servicios municipales siguen la riqueza de la ciudad", "Les salaires des services municipaux suivent la richesse de la ville", "도시가 부자일수록 공공서비스 인건비도 올라감", "Płace w usługach miejskich rosną wraz z zamożnością miasta", "Salários dos serviços municipais acompanham a riqueza da cidade", "Зарплаты в городских службах растут вместе с благосостоянием города", "城市越富裕，公共服务工资越高" },
            ["opt_ageing"] = new[] { "Aging infrastructure: old networks cost more to maintain", "Alternde Infrastruktur: alte Netze kosten mehr Unterhalt", "Infraestructura envejecida: las redes viejas cuestan más de mantener", "Infrastructures vieillissantes : les vieux réseaux coûtent plus cher à entretenir", "낡은 인프라: 오래된 도로·배관일수록 유지비 폭탄", "Starzejąca się infrastruktura: stare sieci drożej utrzymać", "Infraestrutura envelhecida: redes antigas custam mais para manter", "Стареющая инфраструктура: старые сети дороже в содержании", "基础设施老化：越旧的路网和管线维护越贵" },
            ["opt_comp_scale"] = new[] { "Compensation multiplier", "Entschädigungsfaktor", "Multiplicador de indemnización", "Multiplicateur d'indemnisation", "보상금 배율", "Mnożnik odszkodowań", "Multiplicador de indenização", "Множитель компенсации", "补偿倍数" },
            ["opt_road_scale"] = new[] { "Road upkeep multiplier", "Faktor Straßenunterhalt", "Multiplicador de mantenimiento vial", "Multiplicateur d'entretien des routes", "도로 유지비 배율", "Mnożnik utrzymania dróg", "Multiplicador de manutenção de vias", "Множитель содержания дорог", "道路维护倍数" },
            ["opt_control"] = new[] { "Rent control: share of each rent increase blocked", "Mietpreisbremse: blockierter Anteil jeder Mieterhöhung", "Control de alquiler: parte bloqueada de cada subida", "Encadrement des loyers : part bloquée de chaque hausse", "월세 인상 브레이크: 인상분을 막아 주는 비율", "Kontrola czynszów: zablokowana część każdej podwyżki", "Controle de aluguel: parcela bloqueada de cada aumento", "Контроль аренды: доля блокируемого повышения", "租金管制：每次涨租被抑制的比例" },
            ["opt_subsidy"] = new[] { "Housing subsidies: share of rent paid by the city", "Wohngeld: Anteil der Miete, den die Stadt zahlt", "Subsidios a la vivienda: parte del alquiler que paga la ciudad", "Aides au logement : part du loyer payée par la ville", "월세 보조: 시가 대신 내 주는 월세 비율", "Dopłaty mieszkaniowe: część czynszu płacona przez miasto", "Subsídio habitacional: parcela do aluguel paga pela cidade", "Жилищные субсидии: доля аренды, оплачиваемая городом", "住房补贴：由城市承担的租金比例" },
            ["opt_tent_prop"] = new[] { "Tent props: names, file names or Workshop IDs, separated by commas (blank = any prop with \"tent\" in its name)", "Zelt-Props: Namen, Dateinamen oder Workshop-IDs, durch Kommas getrennt (leer = alle Props mit \"tent\" im Namen)", "Objetos de tienda: nombres, nombres de archivo o ID del Workshop, separados por comas (vacío = cualquier objeto con \"tent\" en el nombre)", "Objets tente : noms, noms de fichier ou ID du Workshop, séparés par des virgules (vide = tout objet contenant \"tent\" dans son nom)", "텐트 소품: 이름, 파일 이름, 창작마당 ID를 쉼표로 구분 (비워 두면 이름에 \"tent\" 들어간 소품 아무거나)", "Rekwizyty namiotów: nazwy, nazwy plików lub ID z Warsztatu, oddzielone przecinkami (puste = każdy rekwizyt z \"tent\" w nazwie)", "Objetos de barraca: nomes, nomes de arquivo ou IDs do Workshop, separados por vírgulas (vazio = qualquer objeto com \"tent\" no nome)", "Пропы палаток: названия, имена файлов или ID в Мастерской через запятую (пусто — любой проп со словом \"tent\" в названии)", "帐篷道具：名称、文件名或创意工坊 ID，用逗号分隔（留空则使用名称中含有\"tent\"的任意道具）" },
            ["opt_tent_cap"] = new[] { "Maximum tents shown on the map", "Höchstzahl sichtbarer Zelte auf der Karte", "Número máximo de tiendas visibles en el mapa", "Nombre maximal de tentes visibles sur la carte", "지도에 보이는 텐트 최대 개수", "Maksymalna liczba namiotów widocznych na mapie", "Número máximo de barracas visíveis no mapa", "Максимум палаток на карте", "地图上显示的帐篷数量上限" },
            ["pol_control"] = new[] { "Rent Control", "Mietpreisbremse", "Control de alquileres", "Encadrement des loyers", "월세 인상 브레이크", "Kontrola czynszów", "Controle de aluguéis", "Контроль арендной платы", "租金管制" },
            ["pol_control_desc"] = new[] {
                "Slows rent increases each year. Tenants are happier while their rent is below market, but the city collects less tax from those buildings, new buildings appear and upgrade more slowly, and buildings far below market can get '{0}' and end up abandoned.",
                "Bremst jährliche Mieterhöhungen. Mieter sind zufriedener, solange ihre Miete unter dem Markt liegt, aber die Stadt nimmt in diesen Gebäuden weniger Steuern ein, neue Gebäude entstehen und werten langsamer auf, und Gebäude weit unter Marktniveau können „{0}“ bekommen und verlassen werden.",
                "Frena las subidas anuales del alquiler. Los inquilinos son más felices mientras pagan menos que el mercado, pero la ciudad recauda menos impuestos de esos edificios, los edificios nuevos aparecen y mejoran más despacio, y los que quedan muy por debajo del mercado pueden sufrir «{0}» y acabar abandonados.",
                "Freine les hausses annuelles de loyer. Les locataires sont plus heureux tant que leur loyer reste sous le marché, mais la ville perçoit moins d'impôts sur ces bâtiments, les nouveaux bâtiments apparaissent et s'améliorent plus lentement, et ceux très en dessous du marché peuvent avoir « {0} » et finir abandonnés.",
                "해마다 오르는 월세에 브레이크를 겁니다. 시세보다 싸게 사는 동안 세입자들은 싱글벙글하지만, 그 건물에서 걷히는 세금은 줄고 새 건물은 느릿느릿 들어서며 업그레이드도 더뎌집니다. 시세보다 너무 싼 건물은 '{0}' 문제가 생겨서 버려질 수도 있어요.",
                "Spowalnia coroczne podwyżki czynszów. Najemcy są szczęśliwsi, dopóki płacą poniżej rynku, ale miasto zbiera mniej podatków z tych budynków, nowe budynki powstają i rozwijają się wolniej, a budynki mocno poniżej rynku mogą dostać „{0}” i zostać opuszczone.",
                "Desacelera os aumentos anuais de aluguel. Os inquilinos ficam mais felizes enquanto pagam abaixo do mercado, mas a cidade arrecada menos impostos desses prédios, novos prédios surgem e melhoram mais devagar, e os que ficam muito abaixo do mercado podem ter \"{0}\" e acabar abandonados.",
                "Замедляет ежегодный рост аренды. Жильцы счастливее, пока платят ниже рынка, но город собирает с этих зданий меньше налогов, новые здания появляются и улучшаются медленнее, а здания, сильно отстающие от рынка, могут получить «{0}» и опустеть.",
                "减缓每年的租金上涨。租金低于市场水平时租户更快乐，但城市从这些建筑收取的税收减少，新建筑出现和升级都会变慢，租金远低于市场的建筑可能出现“{0}”并最终被废弃。" },
            ["pol_subsidy"] = new[] { "Housing Subsidies", "Wohngeld", "Subsidios a la vivienda", "Aides au logement", "월세 좀 보태 드립니다", "Dopłaty mieszkaniowe", "Subsídio habitacional", "Жилищные субсидии", "住房补贴" },
            ["pol_subsidy_desc"] = new[] {
                "The city pays part of every tenant's rent. Tenants are happier and can afford better homes, but the policy is expensive.",
                "Die Stadt übernimmt einen Teil der Miete aller Mieter. Mieter sind zufriedener und können sich bessere Wohnungen leisten, aber die Richtlinie ist teuer.",
                "La ciudad paga parte del alquiler de cada inquilino. Los inquilinos son más felices y pueden permitirse mejores viviendas, pero la ley es cara.",
                "La ville paie une partie du loyer de chaque locataire. Les locataires sont plus heureux et peuvent s'offrir de meilleurs logements, mais la politique coûte cher.",
                "시가 세입자들의 월세 일부를 대신 내 줍니다. 세입자들은 행복해지고 더 좋은 집에도 살 수 있지만, 시 금고는 금방 텅텅 비겠죠.",
                "Miasto płaci część czynszu każdego najemcy. Najemcy są szczęśliwsi i stać ich na lepsze mieszkania, ale ten przepis jest kosztowny.",
                "A cidade paga parte do aluguel de cada inquilino. Os inquilinos ficam mais felizes e podem pagar moradias melhores, mas a política é cara.",
                "Город оплачивает часть аренды каждого жильца. Жильцы счастливее и могут позволить себе жильё получше, но политика стоит дорого.",
                "城市为每位租户承担部分租金。租户更快乐，也能负担更好的住房，但这项政策花费很高。" },
            ["tog_public"] = new[] { "Public Housing", "Sozialwohnungen", "Vivienda pública", "Logement social", "공공주택", "Mieszkania komunalne", "Habitação Pública", "Социальное жильё", "公共住房" },
            ["tog_public_tip"] = new[] { "For the homeless and the poorest families. Tenants pay what they can, the city pays the rest, and the building pays no tax.", "Für Obdachlose und die ärmsten Familien. Mieter zahlen, was sie können, die Stadt den Rest, und das Gebäude zahlt keine Steuern.", "Para personas sin hogar y las familias más pobres. Pagan lo que pueden, la ciudad el resto, y el edificio no paga impuestos.", "Pour les sans-abri et les familles les plus pauvres. Les locataires paient ce qu'ils peuvent, la ville le reste, et le bâtiment ne paie pas d'impôts.", "노숙인과 제일 어려운 가구용. 낼 수 있는 만큼만 내고 나머지는 시가 내요. 건물 세금은 없음.", "Dla bezdomnych i najuboższych rodzin. Lokatorzy płacą, ile mogą, resztę miasto, a budynek nie płaci podatków.", "Para pessoas em situação de rua e as famílias mais pobres. Pagam o que podem, a cidade paga o resto, e o prédio não paga impostos.", "Для бездомных и самых бедных семей. Жильцы платят сколько могут, остальное — город, налогов здание не платит.", "面向无家可归者和最贫困的家庭。住户量力付租，其余由城市承担，建筑不缴税。" },
            ["tog_shelter"] = new[] { "Homeless Shelter", "Obdachlosenunterkunft", "Albergue para personas sin hogar", "Centre d'hébergement", "노숙인 쉼터", "Schronisko dla bezdomnych", "Abrigo para Pessoas em Situação de Rua", "Приют для бездомных", "流浪者收容所" },
            ["tog_shelter_tip"] = new[] { "Homeless households sleep here at the city's expense. Neighbors won't like it.", "Obdachlose Haushalte schlafen hier auf Kosten der Stadt. Die Nachbarn sind nicht begeistert.", "Los hogares sin techo duermen aquí a cargo de la ciudad. A los vecinos no les hará gracia.", "Les foyers sans abri dorment ici aux frais de la ville. Les voisins n'apprécieront pas.", "노숙 가구가 시 돈으로 여기서 지내요. 이웃들은 별로 안 좋아함.", "Bezdomne gospodarstwa nocują tu na koszt miasta. Sąsiedzi nie będą zachwyceni.", "Famílias sem moradia dormem aqui por conta da cidade. Os vizinhos não vão gostar.", "Бездомные семьи живут здесь за счёт города. Соседи будут не в восторге.", "无家可归的家庭在此居住，费用由城市承担。邻居们不会喜欢。" },
            ["pol_control_bill"] = new[] {
                "introduce Rent Control",
                "die Mietpreisbremse einführen",
                "introducir el control de alquileres",
                "instaurer l'encadrement des loyers",
                "월세 인상에 브레이크를 걸기",
                "wprowadzić kontrolę czynszów",
                "instituir o controle de aluguéis",
                "ввести контроль арендной платы",
                "实行租金管制" },
            ["pol_subsidy_bill"] = new[] {
                "fund Housing Subsidies",
                "Wohngeld finanzieren",
                "financiar subsidios a la vivienda",
                "financer les aides au logement",
                "월세 보조금을 지원하기",
                "sfinansować dopłaty mieszkaniowe",
                "financiar o subsídio habitacional",
                "профинансировать жилищные субсидии",
                "资助住房补贴" },
            ["assoc_title"] = new[] { "Neighborhood association", "Nachbarschaftsverein", "Asociación vecinal", "Association de quartier", "주민 협의회", "Stowarzyszenie mieszkańców", "Associação de moradores", "Соседское объединение", "社区居民协会" },
            ["assoc_none"] = new[] { "No association has formed yet.", "Noch hat sich kein Verein gebildet.", "Aún no se ha formado ninguna asociación.", "Aucune association ne s'est encore formée.", "아직 협의회가 없어요.", "Nie powstało jeszcze żadne stowarzyszenie.", "Nenhuma associação se formou ainda.", "Объединение ещё не создано.", "尚未成立居民协会。" },
            ["assoc_forming"] = new[] { "Residents are starting to organize.", "Die Anwohner beginnen sich zu organisieren.", "Los vecinos empiezan a organizarse.", "Les habitants commencent à s'organiser.", "주민들이 슬슬 뭉치기 시작했어요.", "Mieszkańcy zaczynają się organizować.", "Os moradores começam a se organizar.", "Жители начинают объединяться.", "居民们开始组织起来。" },
            ["assoc_outside"] = new[] { "Areas outside districts have no association.", "Gebiete außerhalb von Stadtvierteln haben keinen Verein.", "Las zonas fuera de los distritos no tienen asociación.", "Les zones hors des quartiers n'ont pas d'association.", "지구 밖 동네에는 협의회가 없어요.", "Tereny poza dzielnicami nie mają stowarzyszenia.", "Áreas fora dos distritos não têm associação.", "У территорий вне районов нет объединения.", "区划之外的地区没有居民协会。" },
            ["assoc_strength"] = new[] { "Organization: {0}%", "Organisationsgrad: {0} %", "Organización: {0} %", "Organisation : {0} %", "똘똘 뭉친 정도: {0}%", "Zorganizowanie: {0}%", "Organização: {0}%", "Сплочённость: {0}%", "组织程度：{0}%" },
            ["assoc_gripe"] = new[] { "Grievance: {0}%", "Unmut: {0} %", "Descontento: {0} %", "Mécontentement : {0} %", "불만 게이지: {0}%", "Niezadowolenie: {0}%", "Insatisfação: {0}%", "Недовольство: {0}%", "不满程度：{0}%" },
            ["assoc_demand"] = new[] { "Demand: {0} ({1:# week|# weeks} left)", "Forderung: {0} (noch {1:# Woche|# Wochen})", "Reivindicación: {0} ({1:queda # semana|quedan # semanas})", "Revendication : {0} ({1:# semaine restante|# semaines restantes})", "요구 사항: {0} ({1}주 남음)", "Żądanie: {0} ({1:został # tydzień|zostały # tygodnie|zostało # tygodni})", "Reivindicação: {0} ({1:# semana restante|# semanas restantes})", "Требование: {0} ({1:осталась # неделя|осталось # недели|осталось # недель})", "诉求：{0}（剩余 {1} 周）" },
            ["assoc_strike"] = new[] { "Tenants are on rent strike.", "Die Mieter sind im Mietstreik.", "Los inquilinos están en huelga de alquileres.", "Les locataires sont en grève des loyers.", "세입자들이 월세 파업 중이에요!", "Najemcy prowadzą strajk czynszowy.", "Os inquilinos estão em greve de aluguel.", "Жильцы объявили арендную забастовку.", "租户正在进行租金罢缴。" },
            ["need_transit"] = new[] { "better public transport", "besseren Nahverkehr", "mejor transporte público", "de meilleurs transports publics", "더 편한 대중교통", "lepszy transport publiczny", "transporte público melhor", "лучшего общественного транспорта", "更好的公共交通" },
            ["need_services"] = new[] { "a park, school or clinic nearby", "einen Park, eine Schule oder Klinik in der Nähe", "un parque, escuela o clínica cerca", "un parc, une école ou une clinique à proximité", "가까운 공원·학교·병원", "park, szkoła lub przychodnia w pobliżu", "um parque, escola ou clínica por perto", "парка, школы или поликлиники рядом", "附近的公园、学校或诊所" },
            ["need_rent"] = new[] { "lower rents", "niedrigere Mieten", "alquileres más bajos", "des loyers plus bas", "월세 인하", "niższe czynsze", "aluguéis mais baixos", "снижения арендной платы", "降低租金" },
            ["need_shelter"] = new[] { "help for homeless neighbors", "Hilfe für obdachlose Nachbarn", "ayuda para los vecinos sin hogar", "de l'aide pour les voisins sans abri", "노숙 이웃 돕기", "pomoc dla bezdomnych sąsiadów", "ajuda para os vizinhos em situação de rua", "помощи бездомным соседям", "帮助无家可归的邻居" },
            ["need_quiet"] = new[] { "less traffic and noise", "weniger Verkehr und Lärm", "menos tráfico y ruido", "moins de circulation et de bruit", "덜 막히고 덜 시끄러운 동네", "mniejszy ruch i hałas", "menos trânsito e barulho", "меньше транспорта и шума", "减少交通和噪音" },
            ["need_clean"] = new[] { "less pollution", "weniger Umweltverschmutzung", "menos contaminación", "moins de pollution", "깨끗한 공기와 땅", "mniejsze zanieczyszczenie", "menos poluição", "меньше загрязнения", "减少污染" },
            ["need_nodemolition"] = new[] { "no more demolitions", "keine weiteren Abrisse", "no más demoliciones", "plus aucune démolition", "철거는 이제 그만", "koniec wyburzeń", "chega de demolições", "прекращения сносов", "停止拆迁" },
            ["dbg_title"] = new[] { "City Consequences statistics (Ctrl+Shift+C)", "City Consequences – Statistik (Strg+Umschalt+C)", "Estadísticas de City Consequences (Ctrl+Mayús+C)", "Statistiques City Consequences (Ctrl+Maj+C)", "City Consequences 통계 (Ctrl+Shift+C)", "Statystyki City Consequences (Ctrl+Shift+C)", "Estatísticas do City Consequences (Ctrl+Shift+C)", "Статистика City Consequences (Ctrl+Shift+C)", "City Consequences 统计（Ctrl+Shift+C）" },
            ["dbg_city"] = new[] { "WHOLE CITY", "GANZE STADT", "TODA LA CIUDAD", "TOUTE LA VILLE", "도시 전체", "CAŁE MIASTO", "CIDADE INTEIRA", "ВЕСЬ ГОРОД", "全市" },
            ["dbg_citywide"] = new[] { "Citywide", "Stadtweit", "Toda la ciudad", "Toute la ville", "도시 전체", "Całe miasto", "Cidade inteira", "Весь город", "全市" },
            ["dbg_wait"] = new[] { "Housing statistics appear after the first rent cycle (about 4 in-game weeks).", "Die Wohnstatistik erscheint nach dem ersten Mietzyklus (etwa 4 Spielwochen).", "Las estadísticas de vivienda aparecen tras el primer ciclo de alquiler (unas 4 semanas de juego).", "Les statistiques du logement apparaissent après le premier cycle de loyer (environ 4 semaines de jeu).", "주거 통계는 첫 월세 주기(게임 시간 약 4주)가 지나야 나와요.", "Statystyki mieszkaniowe pojawią się po pierwszym cyklu czynszowym (około 4 tygodnie gry).", "As estatísticas de moradia aparecem após o primeiro ciclo de aluguel (cerca de 4 semanas de jogo).", "Жилищная статистика появится после первого цикла аренды (около 4 игровых недель).", "住房统计会在第一个房租周期（约游戏内 4 周）后出现。" },
            ["dbg_households"] = new[] { "Households: {0} · empty homes: {1} ({2}%)", "Haushalte: {0} · leere Wohnungen: {1} ({2} %)", "Hogares: {0} · viviendas vacías: {1} ({2} %)", "Foyers : {0} · logements vides : {1} ({2} %)", "가구: {0} · 빈집: {1} ({2}%)", "Gospodarstwa: {0} · puste mieszkania: {1} ({2}%)", "Famílias: {0} · moradias vazias: {1} ({2}%)", "Семей: {0} · пустых квартир: {1} ({2}%)", "住户：{0} · 空置住房：{1}（{2}%）" },
            ["dbg_income"] = new[] { "Household income per week: average ₡{0} · lowest fifth ₡{1} · median ₡{2} · top fifth ₡{3}", "Haushaltseinkommen pro Woche: Durchschnitt ₡{0} · unteres Fünftel ₡{1} · Median ₡{2} · oberes Fünftel ₡{3}", "Ingresos semanales por hogar: media ₡{0} · quinto inferior ₡{1} · mediana ₡{2} · quinto superior ₡{3}", "Revenu hebdomadaire des foyers : moyenne ₡{0} · cinquième le plus bas ₡{1} · médiane ₡{2} · cinquième le plus haut ₡{3}", "가구 주간 소득: 평균 ₡{0} · 하위 20% ₡{1} · 중간값 ₡{2} · 상위 20% ₡{3}", "Tygodniowy dochód gospodarstw: średnio ₡{0} · najniższa piąta ₡{1} · mediana ₡{2} · najwyższa piąta ₡{3}", "Renda semanal das famílias: média ₡{0} · quinto mais baixo ₡{1} · mediana ₡{2} · quinto mais alto ₡{3}", "Недельный доход семей: в среднем ₡{0} · нижние 20% ₡{1} · медиана ₡{2} · верхние 20% ₡{3}", "每户每周收入：平均 ₡{0} · 最低 20% ₡{1} · 中位数 ₡{2} · 最高 20% ₡{3}" },
            ["dbg_market"] = new[] { "Housing market: rents ×{0:0.00} (×0.85 when homes stand empty, ×1.25 in a shortage)", "Wohnungsmarkt: Mieten ×{0:0.00} (×0,85 bei Leerstand, ×1,25 bei Wohnungsnot)", "Mercado de vivienda: alquileres ×{0:0.00} (×0,85 con pisos vacíos, ×1,25 con escasez)", "Marché du logement : loyers ×{0:0.00} (×0,85 avec des logements vides, ×1,25 en pénurie)", "주택 시장: 월세 ×{0:0.00} (빈집 많으면 ×0.85, 집 부족하면 ×1.25)", "Rynek mieszkaniowy: czynsze ×{0:0.00} (×0,85 przy pustostanach, ×1,25 przy niedoborze)", "Mercado imobiliário: aluguéis ×{0:0.00} (×0,85 com imóveis vazios, ×1,25 com escassez)", "Рынок жилья: аренда ×{0:0.00} (×0,85 при пустующем жилье, ×1,25 при дефиците)", "住房市场：租金 ×{0:0.00}（空置多时 ×0.85，短缺时 ×1.25）" },
            ["dbg_rent"] = new[] { "Average rent: ₡{0} per week · average share of income: {1}%", "Durchschnittsmiete: ₡{0} pro Woche · durchschnittlicher Anteil am Einkommen: {1} %", "Alquiler medio: ₡{0} a la semana · parte media de los ingresos: {1} %", "Loyer moyen : ₡{0} par semaine · part moyenne du revenu : {1} %", "평균 월세: 주당 ₡{0} · 소득 대비 평균 비율: {1}%", "Średni czynsz: ₡{0} tygodniowo · średni udział w dochodzie: {1}%", "Aluguel médio: ₡{0} por semana · parcela média da renda: {1}%", "Средняя аренда: ₡{0} в неделю · средняя доля дохода: {1}%", "平均租金：每周 ₡{0} · 平均占收入比例：{1}%" },
            ["dbg_notices"] = new[] { "Eviction notices: {0} · in public housing: {1}%", "Kündigungen: {0} · in Sozialwohnungen: {1} %", "Avisos de desahucio: {0} · en vivienda pública: {1} %", "Avis d'expulsion : {0} · en logement social : {1} %", "퇴거 통보: {0} · 공공주택 거주: {1}%", "Wypowiedzenia: {0} · w mieszkaniach komunalnych: {1}%", "Avisos de despejo: {0} · em habitação pública: {1}%", "Уведомлений о выселении: {0} · в социальном жилье: {1}%", "驱逐通知：{0} · 住在公共住房：{1}%" },
            ["dbg_rich"] = new[] { "Rich households: {0} · super rich: {1}", "Reiche Haushalte: {0} · superreich: {1}", "Hogares ricos: {0} · superricos: {1}", "Foyers riches : {0} · très riches : {1}", "부자 가구: {0} · 초부자: {1}", "Bogate gospodarstwa: {0} · superbogate: {1}", "Famílias ricas: {0} · super-ricas: {1}", "Богатых семей: {0} · сверхбогатых: {1}", "富裕住户：{0} · 超级富豪：{1}" },
            ["dbg_homeless"] = new[] { "Homeless households: {0} (street {1}, car {2}, in shelters {3}, settled {4})", "Obdachlose Haushalte: {0} (Straße {1}, Auto {2}, in Unterkünften {3}, geblieben {4})", "Hogares sin techo: {0} (calle {1}, coche {2}, en albergues {3}, asentados {4})", "Foyers sans abri : {0} (rue {1}, voiture {2}, en centre {3}, installés {4})", "노숙 가구: {0} (거리 {1}, 차 {2}, 쉼터 {3}, 정착 {4})", "Bezdomne gospodarstwa: {0} (ulica {1}, auto {2}, w schroniskach {3}, osiadłe {4})", "Famílias sem moradia: {0} (rua {1}, carro {2}, em abrigos {3}, fixadas {4})", "Бездомных семей: {0} (улица {1}, машина {2}, в приютах {3}, осели {4})", "无家可归住户：{0}（街头 {1}，车里 {2}，收容所 {3}，已留下 {4}）" },
            ["dbg_walk"] = new[] { "Average walkability: {0} · road upkeep network factor: ×{1}", "Durchschnittliche Fußläufigkeit: {0} · Netzfaktor Straßenunterhalt: ×{1}", "Caminabilidad media: {0} · factor de red del mantenimiento vial: ×{1}", "Marchabilité moyenne : {0} · facteur réseau de l'entretien routier : ×{1}", "평균 보행성: {0} · 도로 유지비 네트워크 배율: ×{1}", "Średnia dostępność pieszo: {0} · współczynnik sieci utrzymania dróg: ×{1}", "Caminhabilidade média: {0} · fator de rede da manutenção viária: ×{1}", "Средняя пешая доступность: {0} · сетевой множитель содержания дорог: ×{1}", "平均步行可达性：{0} · 道路维护网络系数：×{1}" },
            ["dbg_building"] = new[] { "SELECTED BUILDING", "AUSGEWÄHLTES GEBÄUDE", "EDIFICIO SELECCIONADO", "BÂTIMENT SÉLECTIONNÉ", "선택한 건물", "WYBRANY BUDYNEK", "PRÉDIO SELECIONADO", "ВЫБРАННОЕ ЗДАНИЕ", "所选建筑" },
            ["dbg_none"] = new[] { "Select a residential building to see its households.", "Wähle ein Wohngebäude, um seine Haushalte zu sehen.", "Selecciona un edificio residencial para ver sus hogares.", "Sélectionnez un bâtiment résidentiel pour voir ses foyers.", "주거 건물을 선택하면 가구 정보가 보여요.", "Wybierz budynek mieszkalny, aby zobaczyć jego gospodarstwa.", "Selecione um prédio residencial para ver as famílias.", "Выберите жилое здание, чтобы увидеть его семьи.", "选择一栋住宅建筑以查看其住户。" },
            ["dbg_building_rent"] = new[] { "Rent ₡{0} · market ₡{1} · empty-home discount {2}% · desirability {3}% · walkability {4}", "Miete ₡{0} · Markt ₡{1} · Leerstandsrabatt {2} % · Attraktivität {3} % · Fußläufigkeit {4}", "Alquiler ₡{0} · mercado ₡{1} · descuento por vacíos {2} % · atractivo {3} % · caminabilidad {4}", "Loyer ₡{0} · marché ₡{1} · remise pour vacance {2} % · attractivité {3} % · marchabilité {4}", "월세 ₡{0} · 시세 ₡{1} · 공실 할인 {2}% · 선호도 {3}% · 보행성 {4}", "Czynsz ₡{0} · rynek ₡{1} · rabat za pustostany {2}% · atrakcyjność {3}% · dostępność pieszo {4}", "Aluguel ₡{0} · mercado ₡{1} · desconto por vacância {2}% · atratividade {3}% · caminhabilidade {4}", "Аренда ₡{0} · рынок ₡{1} · скидка за пустующие {2}% · привлекательность {3}% · пешая доступность {4}", "租金 ₡{0} · 市场 ₡{1} · 空置折扣 {2}% · 吸引力 {3}% · 步行可达性 {4}" },
            ["dbg_household"] = new[] { "Household {0}: {1:# person|# people} · income ₡{2} · rent is {3}% of income{4}", "Haushalt {0}: {1:# Person|# Personen} · Einkommen ₡{2} · Miete {3} % des Einkommens{4}", "Hogar {0}: {1:# persona|# personas} · ingresos ₡{2} · el alquiler es el {3} % de los ingresos{4}", "Foyer {0} : {1:# personne|# personnes} · revenu ₡{2} · loyer {3} % du revenu{4}", "가구 {0}: {1}명 · 소득 ₡{2} · 월세가 소득의 {3}%{4}", "Gospodarstwo {0}: {1} os. · dochód ₡{2} · czynsz to {3}% dochodu{4}", "Família {0}: {1:# pessoa|# pessoas} · renda ₡{2} · aluguel é {3}% da renda{4}", "Семья {0}: {1} чел. · доход ₡{2} · аренда {3}% дохода{4}", "住户 {0}：{1} 人 · 收入 ₡{2} · 房租占收入 {3}%{4}" },
            ["dbg_super"] = new[] { " · super rich", " · superreich", " · superrico", " · très riche", " · 초부자", " · superbogate", " · super-rica", " · сверхбогатая", " · 超级富豪" },
            ["dbg_rich_tag"] = new[] { " · rich", " · reich", " · rico", " · riche", " · 부자", " · bogate", " · rica", " · богатая", " · 富裕" },
            ["dbg_notice"] = new[] { " · eviction notice", " · Kündigung", " · aviso de desahucio", " · avis d'expulsion", " · 퇴거 통보", " · wypowiedzenie", " · aviso de despejo", " · уведомление о выселении", " · 驱逐通知" },
            ["dbg_more"] = new[] { "…and {0:# more household|# more households}", "…und {0:# weiterer Haushalt|# weitere Haushalte}", "…y {0:# hogar más|# hogares más}", "…et {0:# autre foyer|# autres foyers}", "…외 {0}가구 더", "…i jeszcze {0:# gospodarstwo|# gospodarstwa|# gospodarstw}", "…e mais {0:# família|# famílias}", "…и ещё {0:# семья|# семьи|# семей}", "……还有 {0} 户" },
            ["dbg_districts"] = new[] { "DISTRICTS", "STADTVIERTEL", "DISTRITOS", "QUARTIERS", "지구", "DZIELNICE", "DISTRITOS", "РАЙОНЫ", "区划" },
            ["dbg_nodistrict"] = new[] { "Outside districts", "Außerhalb von Stadtvierteln", "Fuera de los distritos", "Hors des quartiers", "지구 밖", "Poza dzielnicami", "Fora dos distritos", "Вне районов", "区划之外" },
            ["dbg_district"] = new[] { "{0}: rent ₡{1} · income ₡{2} · rent share {3}% · empty {4}%{5}", "{0}: Miete ₡{1} · Einkommen ₡{2} · Mietanteil {3} % · leer {4} %{5}", "{0}: alquiler ₡{1} · ingresos ₡{2} · parte en alquiler {3} % · vacías {4} %{5}", "{0} : loyer ₡{1} · revenu ₡{2} · part du loyer {3} % · vides {4} %{5}", "{0}: 월세 ₡{1} · 소득 ₡{2} · 월세 비율 {3}% · 빈집 {4}%{5}", "{0}: czynsz ₡{1} · dochód ₡{2} · udział czynszu {3}% · puste {4}%{5}", "{0}: aluguel ₡{1} · renda ₡{2} · parcela do aluguel {3}% · vazias {4}%{5}", "{0}: аренда ₡{1} · доход ₡{2} · доля аренды {3}% · пустует {4}%{5}", "{0}：租金 ₡{1} · 收入 ₡{2} · 房租占比 {3}% · 空置 {4}%{5}" },
            ["dbg_mood"] = new[] { " · organization {0}% · grievance {1}%", " · Organisationsgrad {0} % · Unmut {1} %", " · organización {0} % · descontento {1} %", " · organisation {0} % · mécontentement {1} %", " · 조직력 {0}% · 불만 {1}%", " · zorganizowanie {0}% · niezadowolenie {1}%", " · organização {0}% · insatisfação {1}%", " · сплочённость {0}% · недовольство {1}%", " · 组织程度 {0}% · 不满 {1}%" },
            ["dbg_demand"] = new[] { " · wants {0}", " · fordert {0}", " · pide {0}", " · demande {0}", " · 요구: {0}", " · żąda: {0}", " · pede {0}", " · требует: {0}", " · 诉求：{0}" },
            ["dbg_doorstep"] = new[] { "Doorstep pressure: {0}% of the abandonment point · street homeless nearby: {1}", "Druck vor der Haustür: {0} % bis zur Aufgabe · Obdachlose auf der Straße in der Nähe: {1}", "Presión en la puerta: {0} % del punto de abandono · personas sin hogar en la calle cerca: {1}", "Pression devant la porte : {0} % du seuil d'abandon · sans-abri à la rue à proximité : {1}", "현관 앞 압박: 버려지기까지 {0}% · 근처 거리 노숙: {1}", "Presja pod drzwiami: {0}% progu opuszczenia · bezdomni na ulicy w pobliżu: {1}", "Pressão na porta: {0}% do ponto de abandono · pessoas em situação de rua por perto: {1}", "Давление у входа: {0}% до заброшенности · бездомных на улице рядом: {1}", "门口压力：距离废弃 {0}% · 附近露宿街头者：{1}" },
            ["notif_doorstep_title"] = new[] { "Homeless on the doorstep!", "Obdachlose vor der Tür!", "¡Personas sin hogar en la puerta!", "Des sans-abri devant la porte !", "문 앞에 노숙인이 있어요!", "Bezdomni pod drzwiami!", "Pessoas em situação de rua na porta!", "Бездомные у порога!", "门口有无家可归者！" },
            ["notif_rent_title"] = new[] { "Can't afford rent!", "Miete nicht bezahlbar!", "¡No pueden pagar el alquiler!", "Loyer trop cher !", "월세 감당 불가!", "Nie stać ich na czynsz!", "Não dá pra pagar o aluguel!", "Не по карману аренда!", "交不起房租！" },
            ["rent_alarm_minor"] = new[] { "Most households here can't make rent and have eviction notices", "Die meisten Haushalte hier können die Miete nicht zahlen und haben eine Räumungsankündigung", "La mayoría de los hogares de aquí no pueden pagar el alquiler y tienen aviso de desahucio", "La plupart des foyers ici ne peuvent pas payer le loyer et ont reçu un avis d'expulsion", "여기 사는 집 대부분이 월세를 못 내서 퇴거 통지 받았어", "Większość gospodarstw tutaj nie płaci czynszu i ma nakaz eksmisji", "A maioria das famílias daqui não consegue pagar o aluguel e recebeu aviso de despejo", "Большинство семей здесь не тянут аренду и получили уведомление о выселении", "这里大多数家庭付不起房租，已收到驱逐通知" },
            ["rent_alarm_major"] = new[] { "Almost every household here is about to be evicted", "Fast allen Haushalten hier droht die Zwangsräumung", "Casi todos los hogares de aquí están a punto de ser desahuciados", "Presque tous les foyers ici sont sur le point d'être expulsés", "여기 거의 모든 집이 곧 쫓겨날 판이야", "Prawie wszystkim gospodarstwom tutaj grozi eksmisja", "Quase todas as famílias daqui estão prestes a ser despejadas", "Почти все семьи здесь вот-вот выселят", "这里几乎每户都即将被驱逐" },
            ["doorstep_minor"] = new[] { "Homeless camps nearby: people here are complaining", "Obdachlosenlager in der Nähe: Die Leute hier beschweren sich", "Campamentos de personas sin hogar cerca: la gente de aquí se queja", "Campements de sans-abri à proximité : les occupants se plaignent", "근처에 노숙 텐트촌: 여기 사람들 불만이 많음", "Obozowiska bezdomnych w pobliżu: ludzie tutaj się skarżą", "Acampamentos de pessoas em situação de rua por perto: o pessoal daqui está reclamando", "Рядом лагеря бездомных: здесь жалуются", "附近有无家可归者的营地：这里的人在抱怨" },
            ["doorstep_major"] = new[] { "Homeless camps nearby: the occupants are about to leave", "Obdachlosenlager in der Nähe: Die Nutzer stehen kurz vor dem Auszug", "Campamentos de personas sin hogar cerca: los ocupantes están a punto de irse", "Campements de sans-abri à proximité : les occupants sont sur le point de partir", "근처에 노숙 텐트촌: 곧 다들 떠날 기세", "Obozowiska bezdomnych w pobliżu: użytkownicy zaraz się wyprowadzą", "Acampamentos de pessoas em situação de rua por perto: os ocupantes estão prestes a sair", "Рядом лагеря бездомных: жильцы вот-вот съедут", "附近有无家可归者的营地：使用者即将搬走" },
            ["pol_criminal"] = new[] { "Criminalize Homelessness", "Obdachlosigkeit unter Strafe stellen", "Criminalizar la falta de vivienda", "Criminaliser le sans-abrisme", "노숙하면 잡아갑니다", "Penalizacja bezdomności", "Criminalizar a situação de rua", "Запрет бродяжничества", "露宿入罪" },
            ["pol_criminal_bill"] = new[] { "criminalize homelessness", "Obdachlosigkeit unter Strafe stellen", "criminalizar la falta de vivienda", "criminaliser le sans-abrisme", "노숙을 처벌하기", "wprowadzić penalizację bezdomności", "criminalizar a situação de rua", "ввести наказание за бродяжничество", "将露宿街头入罪" },
            ["pol_criminal_desc"] = new[] { "Police arrest people sleeping on the street or in their cars. Camps clear and neighbors feel safer, but every arrest ties up police cars and jail cells, and people released from jail can't get a job for a while.", "Die Polizei nimmt Menschen fest, die auf der Straße oder im Auto schlafen. Lager verschwinden und die Nachbarn fühlen sich sicherer, aber jede Festnahme bindet Streifenwagen und Zellen, und wer aus der Haft kommt, findet eine Weile keine Arbeit.", "La policía detiene a quienes duermen en la calle o en su coche. Los campamentos desaparecen y los vecinos se sienten más seguros, pero cada detención ocupa coches patrulla y celdas, y quien sale de la cárcel no puede conseguir trabajo durante un tiempo.", "La police arrête les personnes qui dorment dans la rue ou dans leur voiture. Les campements disparaissent et les voisins se sentent plus en sécurité, mais chaque arrestation mobilise voitures de police et cellules, et les personnes libérées ne trouvent pas de travail pendant un moment.", "길이나 차에서 자는 사람을 경찰이 잡아감. 텐트촌은 사라지고 이웃들은 안심하지만, 체포할 때마다 순찰차랑 감방이 묶이고, 출소한 사람은 한동안 일자리를 못 구함.", "Policja aresztuje osoby śpiące na ulicy lub w samochodach. Obozowiska znikają, a sąsiedzi czują się bezpieczniej, ale każde zatrzymanie blokuje radiowozy i cele, a osoby wychodzące z więzienia przez jakiś czas nie mogą znaleźć pracy.", "A polícia prende quem dorme na rua ou no carro. Os acampamentos somem e os vizinhos se sentem mais seguros, mas cada prisão ocupa viaturas e celas, e quem sai da cadeia fica um tempo sem conseguir emprego.", "Полиция задерживает тех, кто ночует на улице или в машине. Лагеря исчезают, соседи чувствуют себя спокойнее, но каждый арест занимает патрульные машины и камеры, а вышедшие из тюрьмы какое-то время не могут найти работу.", "警察会逮捕睡在街头或车里的人。营地消失了，邻居们也更安心，但每次逮捕都会占用警车和牢房，出狱的人在一段时间内找不到工作。" },
            ["opt_record_weeks"] = new[] { "Weeks a released prisoner can't get a job (0 = off)", "Wochen, in denen Entlassene keine Arbeit finden (0 = aus)", "Semanas sin poder trabajar tras salir de la cárcel (0 = desactivado)", "Semaines sans travail possible après la prison (0 = désactivé)", "출소 후 일자리를 못 구하는 주 수 (0 = 끔)", "Tygodnie bez pracy po wyjściu z więzienia (0 = wyłączone)", "Semanas sem conseguir emprego após sair da prisão (0 = desligado)", "Недель без работы после освобождения (0 — выкл.)", "出狱后无法就业的周数（0 = 关闭）" },
            ["dbg_records"] = new[] { "Prison records: {0} in jail · {1} barred from work · homeless households in jail: {2}", "Vorstrafen: {0} in Haft · {1} finden keine Arbeit · obdachlose Haushalte in Haft: {2}", "Antecedentes: {0} en la cárcel · {1} sin poder trabajar · hogares sin vivienda en la cárcel: {2}", "Casiers : {0} en prison · {1} privés d'emploi · foyers sans abri en prison : {2}", "전과: 수감 {0}명 · 취업 불가 {1}명 · 수감된 노숙 가구: {2}", "Kartoteki: {0} w więzieniu · {1} bez szans na pracę · bezdomne gospodarstwa w więzieniu: {2}", "Fichas: {0} presos · {1} sem poder trabalhar · famílias sem teto presas: {2}", "Судимости: {0} в заключении · {1} не могут устроиться на работу · бездомных семей в заключении: {2}", "案底：在押 {0} 人 · 无法就业 {1} 人 · 在押的无家可归家庭：{2}" },
            ["dbg_owner"] = new[] { " · owns home", " · Eigentum", " · propietario", " · propriétaire", " · 자가", " · właściciel", " · casa própria", " · собственник", " · 自有住房" },
            ["map_rent"] = new[] { "Rent", "Miete", "Alquiler", "Loyer", "월세", "Czynsz", "Aluguel", "Аренда", "租金" },
            ["map_low"] = new[] { "Low", "Niedrig", "Bajo", "Bas", "낮음", "Niski", "Baixo", "Низкая", "低" },
            ["map_high"] = new[] { "High", "Hoch", "Alto", "Élevé", "높음", "Wysoki", "Alto", "Высокая", "高" },
            ["map_public"] = new[] { "Public housing", "Sozialwohnungen", "Vivienda pública", "Logement social", "공공주택", "Mieszkania komunalne", "Habitação popular", "Социальное жильё", "公共住房" },
            ["map_avg"] = new[] { "Average rent: ₡{0} per week", "Durchschnittsmiete: ₡{0} pro Woche", "Alquiler medio: ₡{0} por semana", "Loyer moyen : ₡{0} par semaine", "평균 월세: 주당 ₡{0}", "Średni czynsz: ₡{0} tygodniowo", "Aluguel médio: ₡{0} por semana", "Средняя аренда: ₡{0} в неделю", "平均租金：每周 ₡{0}" },
            ["map_top"] = new[] { "Highest possible rent: ₡{0} per week", "Höchstmögliche Miete: ₡{0} pro Woche", "Alquiler máximo posible: ₡{0} por semana", "Loyer maximal possible : ₡{0} par semaine", "가능한 최고 월세: 주당 ₡{0}", "Najwyższy możliwy czynsz: ₡{0} tygodniowo", "Aluguel máximo possível: ₡{0} por semana", "Максимально возможная аренда: ₡{0} в неделю", "可能的最高租金：每周 ₡{0}" },
            ["map_none"] = new[] { "No rents calculated yet", "Noch keine Mieten berechnet", "Todavía no hay alquileres calculados", "Aucun loyer calculé pour l'instant", "아직 계산된 월세가 없어요", "Brak jeszcze obliczonych czynszów", "Nenhum aluguel calculado ainda", "Аренда ещё не рассчитана", "尚未计算租金" },
            ["dbg_tents"] = new[] { "Tents shown: {0} of {1} allowed · tent props found: {2}", "Sichtbare Zelte: {0} von {1} erlaubten · gefundene Zelt-Props: {2}", "Tiendas visibles: {0} de {1} permitidas · objetos de tienda encontrados: {2}", "Tentes visibles : {0} sur {1} autorisées · objets tente trouvés : {2}", "보이는 텐트: {0}/{1}개 · 찾은 텐트 소품: {2}개", "Widoczne namioty: {0} z {1} dozwolonych · znalezione rekwizyty namiotów: {2}", "Barracas visíveis: {0} de {1} permitidas · objetos de barraca encontrados: {2}", "Палаток на карте: {0} из {1} допустимых · найдено пропов палаток: {2}", "显示的帐篷：{0}/{1} · 找到的帐篷道具：{2} 个" },
            ["dbg_strike"] = new[] { " · rent strike", " · Mietstreik", " · huelga de alquileres", " · grève des loyers", " · 월세 파업", " · strajk czynszowy", " · greve de aluguel", " · арендная забастовка", " · 罢缴房租" },
            ["map_homeless"] = new[] { "Homelessness", "Obdachlosigkeit", "Personas sin hogar", "Sans-abrisme", "노숙", "Bezdomność", "Pessoas em situação de rua", "Бездомность", "无家可归" },
            ["map_hl_low"] = new[] { "Few nearby", "Wenige in der Nähe", "Pocas cerca", "Peu à proximité", "주변에 조금", "Niewielu w pobliżu", "Poucas por perto", "Мало поблизости", "附近较少" },
            ["map_hl_high"] = new[] { "Many nearby", "Viele in der Nähe", "Muchas cerca", "Beaucoup à proximité", "주변에 많음", "Wielu w pobliżu", "Muitas por perto", "Много поблизости", "附近很多" },
            ["map_hl_total"] = new[] { "Homeless households: {0:N0}", "Obdachlose Haushalte: {0:N0}", "Hogares sin techo: {0:N0}", "Foyers sans abri : {0:N0}", "노숙 가구: {0:N0}", "Bezdomne gospodarstwa: {0:N0}", "Famílias sem moradia: {0:N0}", "Бездомных семей: {0:N0}", "无家可归家庭：{0:N0}" },
            ["map_hl_shelters"] = new[] { "In shelters: {0:N0}", "In Unterkünften: {0:N0}", "En albergues: {0:N0}", "En centre d'hébergement : {0:N0}", "쉼터에서 지냄: {0:N0}", "W schroniskach: {0:N0}", "Em abrigos: {0:N0}", "В приютах: {0:N0}", "住在收容所：{0:N0}" },
            ["map_hl_cars"] = new[] { "Living in cars: {0:N0}", "Leben im Auto: {0:N0}", "Viven en el coche: {0:N0}", "Vivent dans leur voiture : {0:N0}", "차에서 지냄: {0:N0}", "Mieszkają w samochodach: {0:N0}", "Vivem no carro: {0:N0}", "Живут в машинах: {0:N0}", "住在车里：{0:N0}" },
            ["map_hl_jail"] = new[] { "In jail: {0:N0}", "Im Gefängnis: {0:N0}", "En la cárcel: {0:N0}", "En prison : {0:N0}", "감옥에 있음: {0:N0}", "W więzieniu: {0:N0}", "Na prisão: {0:N0}", "В тюрьме: {0:N0}", "在狱中：{0:N0}" },
            ["pol_public_all"] = new[] { "Universal Public Housing", "Gemeinnütziges Wohnen für alle", "Vivienda pública universal", "Logement social universel", "모두 공공주택!", "Powszechne mieszkania komunalne", "Habitação pública universal", "Всеобщее социальное жильё", "全民公共住房" },
            ["pol_public_all_desc"] = new[] { "Every home here becomes public housing, except homeless shelters. No one is evicted over rent and the city covers tenants who can't pay, but these homes pay no tax.", "Alle Wohnungen hier werden Sozialwohnungen, außer Obdachlosenunterkünften. Niemand wird wegen der Miete geräumt und die Stadt trägt die Kosten für Mieter, die nicht zahlen können, aber diese Wohnungen zahlen keine Steuern.", "Todas las viviendas de aquí pasan a ser públicas, salvo los albergues. Nadie es desahuciado por el alquiler y la ciudad cubre a quien no puede pagar, pero estas viviendas no pagan impuestos.", "Tous les logements d'ici deviennent sociaux, sauf les centres d'hébergement. Plus personne n'est expulsé pour loyer impayé et la ville couvre ceux qui ne peuvent pas payer, mais ces logements ne paient pas d'impôts.", "쉼터 빼고 여기 모든 집이 공공주택이 돼요. 월세 때문에 쫓겨나는 사람은 없고 못 내는 가구는 시가 챙겨요. 대신 이 집들은 세금을 안 내요.", "Wszystkie mieszkania tutaj stają się komunalne, poza schroniskami. Nikt nie jest eksmitowany za czynsz, a miasto pokrywa koszty tych, których nie stać, ale te budynki nie płacą podatków.", "Todas as moradias daqui viram habitação popular, exceto abrigos. Ninguém é despejado pelo aluguel e a cidade cobre quem não pode pagar, mas essas moradias não pagam impostos.", "Всё жильё здесь становится социальным, кроме приютов. Никого не выселяют за аренду, а город покрывает тех, кто не может платить, но эти дома не платят налогов.", "除收容所外，这里所有住房都变为公共住房。没人会因房租被驱逐，付不起的住户由城市承担，但这些住房不缴税。" },
            ["pol_public_all_bill"] = new[] { "make all housing public", "alle Wohnungen gemeinnützig machen", "hacer pública toda la vivienda", "rendre tout le logement social", "모든 집을 공공주택으로", "uczynić wszystkie mieszkania komunalnymi", "tornar toda a moradia pública", "сделать всё жильё социальным", "让所有住房成为公共住房" },
            ["tog_public_forced"] = new[] { "Every home here is public housing because of the Universal Public Housing policy.", "Wegen der Richtlinie Gemeinnütziges Wohnen für alle ist hier jede Wohnung eine Sozialwohnung.", "Por la política de vivienda pública universal, aquí todas las viviendas son públicas.", "Avec la politique Logement social universel, tous les logements d'ici sont sociaux.", "'모두 공공주택!' 정책 때문에 여기 집은 전부 공공주택이에요.", "Przez zasadę powszechnych mieszkań komunalnych każde mieszkanie tutaj jest komunalne.", "Pela política de habitação pública universal, toda moradia daqui é pública.", "Из-за политики всеобщего социального жилья здесь всё жильё социальное.", "由于全民公共住房政策，这里的所有住房都是公共住房。" },
            ["pol_no_str"] = new[] { "Ban Short-Term Rentals", "Verbot von Ferienwohnungen", "Prohibir pisos turísticos", "Interdire les locations de courte durée", "단기임대 금지", "Zakaz najmu krótkoterminowego", "Proibir aluguel por temporada", "Запрет посуточной аренды", "禁止短租" },
            ["pol_no_str_desc"] = new[] { "Homes can no longer be rented out to tourists by the night, so they go back to residents and rents ease. The city loses the tourist tax those rentals brought in.", "Wohnungen dürfen nicht mehr nächteweise an Touristen vermietet werden, gehen also zurück an Einwohner, und die Mieten entspannen sich. Die Stadt verliert die Touristensteuer dieser Vermietungen.", "Las viviendas ya no pueden alquilarse por noches a turistas, así que vuelven a los residentes y los alquileres se moderan. La ciudad pierde la tasa turística que aportaban.", "Les logements ne peuvent plus être loués à la nuitée aux touristes : ils reviennent aux habitants et les loyers se détendent. La ville perd la taxe de séjour qu'ils rapportaient.", "집을 관광객한테 하룻밤씩 못 빌려줘요. 그래서 집이 주민한테 돌아가고 월세도 좀 내려가요. 대신 시는 관광세 수입을 잃어요.", "Mieszkań nie można już wynajmować turystom na noce, więc wracają do mieszkańców, a czynsze spadają. Miasto traci opłatę turystyczną z tego najmu.", "Moradias não podem mais ser alugadas por diária a turistas, então voltam aos moradores e os aluguéis aliviam. A cidade perde a taxa turística que esses aluguéis rendiam.", "Жильё больше нельзя сдавать туристам посуточно, поэтому оно возвращается жителям, а аренда дешевеет. Город теряет туристический налог с такой аренды.", "住房不能再按晚租给游客，于是回到居民手中，房租随之回落。城市会失去这些短租带来的旅游税。" },
            ["pol_no_str_bill"] = new[] { "ban short-term rentals", "Ferienwohnungen verbieten", "prohibir los pisos turísticos", "interdire les locations de courte durée", "단기임대 금지", "zakazać najmu krótkoterminowego", "proibir o aluguel por temporada", "запретить посуточную аренду", "禁止短租" },
            ["opt_holiday_share"] = new[] { "Vacation rentals: most homes a building can lose to tourists (%, 0 = off)", "Ferienwohnungen: höchster Anteil eines Hauses an Touristen (%, 0 = aus)", "Pisos turísticos: máximo de viviendas de un edificio para turistas (%, 0 = desactivado)", "Locations touristiques : part maximale d'un immeuble louée aux touristes (%, 0 = désactivé)", "단기임대: 건물에서 관광객용으로 빠질 수 있는 최대 비율 (%, 0 = 끄기)", "Najem turystyczny: maksymalny udział mieszkań w budynku dla turystów (%, 0 = wył.)", "Aluguel por temporada: máximo de moradias de um prédio para turistas (%, 0 = desligado)", "Посуточная аренда: максимальная доля квартир дома для туристов (%, 0 = выкл.)", "短租：一栋楼最多被游客占用的住房比例（%，0 = 关闭）" },
            ["opt_holiday_attraction"] = new[] { "Vacation rentals: tourist attraction needed nearby (lower = more vacation rentals)", "Ferienwohnungen: nötige Touristenattraktivität in der Nähe (niedriger = mehr Ferienwohnungen)", "Pisos turísticos: atractivo turístico necesario cerca (menor = más pisos turísticos)", "Locations touristiques : attractivité touristique nécessaire à proximité (plus bas = plus de locations)", "단기임대: 주변에 필요한 관광 매력도 (낮을수록 단기임대 많아짐)", "Najem turystyczny: wymagana atrakcyjność turystyczna w pobliżu (niżej = więcej najmu)", "Aluguel por temporada: atratividade turística necessária por perto (menor = mais aluguéis)", "Посуточная аренда: нужная туристическая привлекательность рядом (меньше = больше аренды)", "短租：附近所需的旅游吸引力（越低短租越多）" },
            ["opt_religion"] = new[] { "Religion and places of worship", "Religion und Gotteshäuser", "Religión y lugares de culto", "Religion et lieux de culte", "종교와 예배 장소", "Religia i miejsca kultu", "Religião e locais de culto", "Религия и места поклонения", "宗教与礼拜场所" },
            ["opt_birth"] = new[] { "Education affects birth rate", "Bildung beeinflusst die Geburtenrate", "La educación afecta a la natalidad", "L'éducation influe sur la natalité", "교육 수준이 출생률에 영향", "Wykształcenie wpływa na dzietność", "A educação afeta a natalidade", "Образование влияет на рождаемость", "教育影响出生率" },
            ["opt_birth_mother"] = new[] { "Based on the mother's education", "Nach Bildung der Mutter", "Según la educación de la madre", "Selon l'éducation de la mère", "엄마의 교육 수준 기준", "Według wykształcenia matki", "Pela educação da mãe", "По образованию матери", "依据母亲的教育程度" },
            ["opt_birth_both"] = new[] { "Based on both parents' education", "Nach Bildung beider Eltern", "Según la educación de ambos padres", "Selon l'éducation des deux parents", "부모 둘 다의 교육 수준 기준", "Według wykształcenia obojga rodziców", "Pela educação de ambos os pais", "По образованию обоих родителей", "依据父母双方的教育程度" },
            ["opt_birth_off"] = new[] { "Off", "Aus", "Desactivado", "Désactivé", "끄기", "Wyłączone", "Desativado", "Выключено", "关闭" },
            ["opt_birth_strength"] = new[] { "Strength of the education effect (%)", "Stärke des Bildungseffekts (%)", "Intensidad del efecto educativo (%)", "Intensité de l'effet de l'éducation (%)", "교육 효과 세기 (%)", "Siła wpływu wykształcenia (%)", "Intensidade do efeito da educação (%)", "Сила влияния образования (%)", "教育影响强度（%）" },
            ["opt_worship_assets"] = new[] { "Assets used as places of worship: {0} (click to clear)", "Als Gotteshaus genutzte Assets: {0} (Klicken zum Zurücksetzen)", "Recursos usados como lugares de culto: {0} (clic para borrar)", "Assets utilisés comme lieux de culte : {0} (cliquer pour effacer)", "예배 장소로 쓰는 에셋: {0}개 (누르면 초기화)", "Obiekty używane jako miejsca kultu: {0} (kliknij, aby wyczyścić)", "Assets usados como locais de culto: {0} (clique para limpar)", "Объектов — мест поклонения: {0} (нажмите, чтобы сбросить)", "用作礼拜场所的资产：{0}（点击清除）" },
            ["map_religion"] = new[] { "Religion", "Religion", "Religión", "Religion", "종교", "Religia", "Religião", "Религия", "宗教" },
            ["map_rel_low"] = new[] { "Poor access", "Schlechter Zugang", "Acceso escaso", "Accès faible", "가기 힘듦", "Słaby dostęp", "Pouco acesso", "Плохой доступ", "难以到达" },
            ["map_rel_high"] = new[] { "Good access", "Guter Zugang", "Buen acceso", "Bon accès", "가기 편함", "Dobry dostęp", "Bom acesso", "Хороший доступ", "方便到达" },
            ["map_rel_worship"] = new[] { "Homes are colored by access to their own faith. Places of worship show their congregation's color:", "Wohnhäuser zeigen den Zugang zum eigenen Glauben. Gotteshäuser erscheinen in der Farbe ihrer Gemeinde:", "Las viviendas muestran el acceso a su propia fe. Los lugares de culto llevan el color de su congregación:", "Les logements indiquent l'accès à leur propre culte. Les lieux de culte prennent la couleur de leur congrégation :", "집 색은 자기 종교 시설에 얼마나 가기 쉬운지 보여 줘요. 예배 장소는 공동체 색으로 표시돼요:", "Domy pokazują dostęp do własnej wiary. Miejsca kultu mają kolor swojej wspólnoty:", "As casas mostram o acesso à própria fé. Os locais de culto aparecem na cor da sua congregação:", "Дома окрашены по доступу к своей вере. Места поклонения — в цвет своей общины:", "住宅颜色表示前往自己信仰场所的便利程度。礼拜场所以其宗教团体的颜色显示：" },
            ["map_rel_edit"] = new[] { "Congregations…", "Gemeinden…", "Congregaciones…", "Congrégations…", "공동체 관리…", "Wspólnoty…", "Congregações…", "Общины…", "宗教团体…" },
            ["rel_total"] = new[] { "Residents: {0:N0} · access to worship: {1}%", "Einwohner: {0:N0} · Zugang zu Gotteshäusern: {1} %", "Residentes: {0:N0} · acceso a lugares de culto: {1} %", "Habitants : {0:N0} · accès aux lieux de culte : {1} %", "주민 {0:N0}명 · 예배 장소 접근성 {1}%", "Mieszkańcy: {0:N0} · dostęp do miejsc kultu: {1}%", "Moradores: {0:N0} · acesso a locais de culto: {1}%", "Жители: {0:N0} · доступ к местам поклонения: {1}%", "居民：{0:N0} · 礼拜场所可达性：{1}%" },
            ["rel_nonrel_line"] = new[] { "{0}: {1}% of residents", "{0}: {1} % der Einwohner", "{0}: {1} % de los residentes", "{0} : {1} % des habitants", "{0}: 주민의 {1}%", "{0}: {1}% mieszkańców", "{0}: {1}% dos moradores", "{0}: {1}% жителей", "{0}：占居民 {1}%" },
            ["rel_line"] = new[] { "{0}: {1}% of residents, {2}% access", "{0}: {1} % der Einwohner, Zugang {2} %", "{0}: {1} % de los residentes, acceso {2} %", "{0} : {1} % des habitants, accès {2} %", "{0}: 주민의 {1}%, 접근성 {2}%", "{0}: {1}% mieszkańców, dostęp {2}%", "{0}: {1}% dos moradores, acesso {2}%", "{0}: {1}% жителей, доступ {2}%", "{0}：占居民 {1}%，可达性 {2}%" },
            ["rel_nonrel"] = new[] { "Non-religious", "Konfessionslos", "No religiosos", "Sans religion", "무교", "Bezwyznaniowi", "Sem religião", "Нерелигиозные", "无宗教信仰" },
            ["rel_interfaith"] = new[] { "Interfaith", "Interreligiös", "Interreligioso", "Interconfessionnel", "종교 간 공동", "Międzywyznaniowe", "Inter-religioso", "Межконфессиональное", "跨宗教" },
            ["rel_interfaith_off"] = new[] { "The Interfaith Facilities policy isn't active here, so this building serves nobody.", "Die Richtlinie Interreligiöse Einrichtungen gilt hier nicht, daher dient dieses Gebäude niemandem.", "La política de instalaciones interreligiosas no está activa aquí, así que este edificio no atiende a nadie.", "La politique Lieux interconfessionnels n'est pas active ici : ce bâtiment ne sert personne.", "여기엔 종교 간 공동 시설 정책이 없어서 아무도 안 와요.", "Zasada obiektów międzywyznaniowych nie obowiązuje tutaj, więc ten budynek nikomu nie służy.", "A política de instalações inter-religiosas não está ativa aqui, então este edifício não atende ninguém.", "Политика межконфессиональных зданий здесь не действует, поэтому здание никому не служит.", "此处未启用跨宗教设施政策，此建筑不服务任何人。" },
            ["rel_festival"] = new[] { "Festival in progress", "Fest im Gange", "Festival en curso", "Fête en cours", "축제 중!", "Trwa święto", "Festival em andamento", "Идёт праздник", "节日进行中" },
            ["rel_title"] = new[] { "Congregations", "Gemeinden", "Congregaciones", "Congrégations", "신도 공동체", "Wspólnoty wyznaniowe", "Congregações", "Общины", "宗教团体" },
            ["rel_newcomers"] = new[] { "Newcomers", "Zuzügler", "Recién llegados", "Nouveaux arrivants", "새로 오는 주민", "Nowi mieszkańcy", "Recém-chegados", "Новоприбывшие", "新居民" },
            ["rel_nonrel_share"] = new[] { "Non-religious share", "Anteil Konfessionsloser", "Proporción no religiosa", "Part sans religion", "무교 비율", "Udział bezwyznaniowych", "Proporção sem religião", "Доля нерелигиозных", "无宗教信仰比例" },
            ["rel_edit"] = new[] { "Edit congregation", "Gemeinde bearbeiten", "Editar congregación", "Modifier la congrégation", "공동체 고르기", "Edytuj wspólnotę", "Editar congregação", "Изменить общину", "编辑宗教团体" },
            ["rel_enabled"] = new[] { "Present in this city", "In dieser Stadt vertreten", "Presente en esta ciudad", "Présente dans cette ville", "이 도시에 있음", "Obecna w tym mieście", "Presente nesta cidade", "Есть в этом городе", "存在于本城市" },
            ["rel_absent"] = new[] { "absent", "nicht vertreten", "ausente", "absente", "없음", "nieobecna", "ausente", "нет в городе", "不存在" },
            ["rel_name"] = new[] { "Name", "Name", "Nombre", "Nom", "이름", "Nazwa", "Nome", "Название", "名称" },
            ["rel_share"] = new[] { "Share of newcomers", "Anteil der Zuzügler", "Proporción de recién llegados", "Part des nouveaux arrivants", "새 주민 중 비율", "Udział wśród nowych mieszkańców", "Proporção de recém-chegados", "Доля среди новоприбывших", "新居民占比" },
            ["rel_cong_default"] = new[] { "Congregation {0}", "Gemeinde {0}", "Congregación {0}", "Congrégation {0}", "공동체 {0}", "Wspólnota {0}", "Congregação {0}", "Община {0}", "宗教团体 {0}" },
            ["rel_practices"] = new[] { "Practices", "Bräuche", "Prácticas", "Pratiques", "관습", "Praktyki", "Práticas", "Обычаи", "习俗" },
            ["rel_rhythm"] = new[] { "Worship rhythm", "Gottesdienstrhythmus", "Ritmo de culto", "Rythme du culte", "예배 주기", "Rytm nabożeństw", "Ritmo de culto", "Ритм богослужений", "礼拜节奏" },
            ["r_weekly"] = new[] { "Weekly worship day", "Wöchentlicher Gottesdiensttag", "Día de culto semanal", "Jour de culte hebdomadaire", "매주 예배일", "Cotygodniowy dzień modlitwy", "Dia de culto semanal", "Еженедельный день служения", "每周礼拜日" },
            ["r_daily"] = new[] { "Daily short visits", "Tägliche kurze Besuche", "Visitas breves diarias", "Courtes visites quotidiennes", "매일 잠깐씩", "Codzienne krótkie wizyty", "Visitas curtas diárias", "Ежедневные короткие посещения", "每日短暂参拜" },
            ["r_fest"] = new[] { "Seasonal festivals", "Saisonale Feste", "Festivales de temporada", "Fêtes saisonnières", "계절 축제", "Święta sezonowe", "Festivais sazonais", "Сезонные праздники", "季节性节日" },
            ["rel_day"] = new[] { "Worship day", "Gottesdiensttag", "Día de culto", "Jour de culte", "예배일", "Dzień modlitwy", "Dia de culto", "День служения", "礼拜日" },
            ["day_0"] = new[] { "Monday", "Montag", "Lunes", "Lundi", "월요일", "Poniedziałek", "Segunda-feira", "Понедельник", "星期一" },
            ["day_1"] = new[] { "Tuesday", "Dienstag", "Martes", "Mardi", "화요일", "Wtorek", "Terça-feira", "Вторник", "星期二" },
            ["day_2"] = new[] { "Wednesday", "Mittwoch", "Miércoles", "Mercredi", "수요일", "Środa", "Quarta-feira", "Среда", "星期三" },
            ["day_3"] = new[] { "Thursday", "Donnerstag", "Jueves", "Jeudi", "목요일", "Czwartek", "Quinta-feira", "Четверг", "星期四" },
            ["day_4"] = new[] { "Friday", "Freitag", "Viernes", "Vendredi", "금요일", "Piątek", "Sexta-feira", "Пятница", "星期五" },
            ["day_5"] = new[] { "Saturday", "Samstag", "Sábado", "Samedi", "토요일", "Sobota", "Sábado", "Суббота", "星期六" },
            ["day_6"] = new[] { "Sunday", "Sonntag", "Domingo", "Dimanche", "일요일", "Niedziela", "Domingo", "Воскресенье", "星期日" },
            ["rel_funeral"] = new[] { "Funeral practice", "Bestattungsbrauch", "Práctica funeraria", "Pratique funéraire", "장례 방식", "Obrządek pogrzebowy", "Prática funerária", "Похоронный обряд", "丧葬习俗" },
            ["f_burial"] = new[] { "Burial", "Erdbestattung", "Entierro", "Inhumation", "매장", "Pochówek w ziemi", "Sepultamento", "Погребение", "土葬" },
            ["f_crem"] = new[] { "Cremation", "Feuerbestattung", "Cremación", "Crémation", "화장", "Kremacja", "Cremação", "Кремация", "火葬" },
            ["f_either"] = new[] { "Either", "Beides", "Cualquiera", "Indifférent", "상관없음", "Dowolny", "Qualquer", "Любой", "均可" },
            ["rel_giving"] = new[] { "Giving", "Spenden", "Donaciones", "Dons", "헌금", "Datki", "Doações", "Пожертвования", "捐献" },
            ["g_tithe"] = new[] { "Tithing", "Zehnt", "Diezmo", "Dîme", "십일조", "Dziesięcina", "Dízimo", "Десятина", "什一奉献" },
            ["g_alms"] = new[] { "Almsgiving", "Almosen", "Limosna", "Aumône", "구제 헌금", "Jałmużna", "Esmola", "Милостыня", "施舍" },
            ["g_none"] = new[] { "None", "Keine", "Ninguno", "Aucun", "없음", "Brak", "Nenhum", "Нет", "无" },
            ["rel_inst"] = new[] { "Institutions", "Einrichtungen", "Instituciones", "Institutions", "부속 시설", "Instytucje", "Instituições", "Учреждения", "附属机构" },
            ["i_schools"] = new[] { "Schools", "Schulen", "Escuelas", "Écoles", "학교", "Szkoły", "Escolas", "Школы", "学校" },
            ["i_clinics"] = new[] { "Clinics", "Kliniken", "Clínicas", "Cliniques", "진료소", "Przychodnie", "Clínicas", "Клиники", "诊所" },
            ["rel_conv"] = new[] { "Conversion", "Konversion", "Conversión", "Conversion", "전도", "Nawracanie", "Conversão", "Обращение", "皈依" },
            ["c_miss"] = new[] { "Missionary", "Missionarisch", "Misionera", "Missionnaire", "적극 전도", "Misyjna", "Missionária", "Миссионерская", "传教型" },
            ["c_hered"] = new[] { "Hereditary", "Erblich", "Hereditaria", "Héréditaire", "대물림", "Dziedziczna", "Hereditária", "Наследственная", "世袭型" },
            ["rel_leisure"] = new[] { "Leisure", "Freizeit", "Ocio", "Loisirs", "여가", "Rozrywka", "Lazer", "Досуг", "休闲" },
            ["l_free"] = new[] { "Unrestricted", "Uneingeschränkt", "Sin restricciones", "Sans restriction", "자유롭게", "Bez ograniczeń", "Sem restrições", "Без ограничений", "无限制" },
            ["l_austere"] = new[] { "Austere", "Enthaltsam", "Austera", "Austère", "금욕적", "Surowa", "Austera", "Аскетичный", "节制" },
            ["rel_family"] = new[] { "Family norms", "Familiennormen", "Normas familiares", "Normes familiales", "가족관", "Normy rodzinne", "Normas familiares", "Семейные нормы", "家庭观念" },
            ["fa_small"] = new[] { "Small families", "Kleine Familien", "Familias pequeñas", "Petites familles", "소가족", "Małe rodziny", "Famílias pequenas", "Малые семьи", "小家庭" },
            ["fa_avg"] = new[] { "Average families", "Durchschnittliche Familien", "Familias medianas", "Familles moyennes", "보통 가족", "Przeciętne rodziny", "Famílias médias", "Средние семьи", "普通家庭" },
            ["fa_large"] = new[] { "Large families", "Große Familien", "Familias numerosas", "Familles nombreuses", "대가족", "Duże rodziny", "Famílias numerosas", "Многодетные семьи", "大家庭" },
            ["rel_edu"] = new[] { "Education norms", "Bildungsnormen", "Normas educativas", "Normes éducatives", "교육관", "Normy edukacyjne", "Normas educacionais", "Нормы образования", "教育观念" },
            ["e_equal"] = new[] { "Equal", "Gleichberechtigt", "Igualitaria", "Égalitaire", "평등", "Równe", "Igualitária", "Равные", "平等" },
            ["e_schol"] = new[] { "Scholarly", "Gelehrt", "Erudita", "Studieuse", "공부 중시", "Uczone", "Erudita", "Учёные", "重学" },
            ["e_trad"] = new[] { "Traditional", "Traditionell", "Tradicional", "Traditionnelle", "전통적", "Tradycyjne", "Tradicional", "Традиционные", "传统" },
            ["e_insul"] = new[] { "Insular", "Abgeschottet", "Cerrada", "Repliée", "폐쇄적", "Zamknięte", "Fechada", "Замкнутые", "封闭" },
            ["rel_serves"] = new[] { "Serves", "Dient", "Atiende a", "Dessert", "다니는 공동체", "Służy", "Atende", "Служит", "服务对象" },
            ["rel_make"] = new[] { "Place of worship", "Gotteshaus", "Lugar de culto", "Lieu de culte", "예배 장소", "Miejsce kultu", "Local de culto", "Место поклонения", "礼拜场所" },
            ["rel_make_tip"] = new[] { "Use this asset as a place of worship. Applies right away to every copy of it.", "Dieses Asset als Gotteshaus nutzen. Gilt sofort für jede Kopie davon.", "Usar este recurso como lugar de culto. Se aplica al instante a todas sus copias.", "Utiliser cet asset comme lieu de culte. S'applique immédiatement à toutes ses copies.", "이 에셋을 예배 장소로 써. 바로 이 에셋 전부에 적용돼!", "Używaj tego obiektu jako miejsca kultu. Od razu dotyczy wszystkich jego kopii.", "Usar este asset como local de culto. Vale na hora para todas as cópias.", "Использовать этот объект как место поклонения. Сразу действует на все его копии.", "将此资产用作礼拜场所。立即对它的所有副本生效。" },
            ["rel_s_cong"] = new[] { "Congregation: {0}", "Gemeinde: {0}", "Congregación: {0}", "Congrégation : {0}", "공동체: {0}", "Wspólnota: {0}", "Congregação: {0}", "Община: {0}", "宗教团体：{0}" },
            ["rel_s_served"] = new[] { "Members served: {0:N0} / {1:N0}", "Betreute Mitglieder: {0:N0} / {1:N0}", "Miembros atendidos: {0:N0} / {1:N0}", "Fidèles servis : {0:N0} / {1:N0}", "오는 신자: {0:N0} / {1:N0}", "Obsłużeni wierni: {0:N0} / {1:N0}", "Membros atendidos: {0:N0} / {1:N0}", "Обслуживается верующих: {0:N0} / {1:N0}", "服务信众：{0:N0} / {1:N0}" },
            ["rel_s_visit"] = new[] { "Visitors: {0:N0}", "Besucher: {0:N0}", "Visitantes: {0:N0}", "Visiteurs : {0:N0}", "방문객: {0:N0}", "Odwiedzający: {0:N0}", "Visitantes: {0:N0}", "Посетители: {0:N0}", "访客：{0:N0}" },
            ["pol_worship_tax"] = new[] { "Tax Religious Property", "Religiöse Gebäude besteuern", "Gravar propiedades religiosas", "Taxer les biens religieux", "종교 시설에도 세금!", "Opodatkowanie obiektów sakralnych", "Tributar propriedades religiosas", "Налог на культовые здания", "对宗教场所征税" },
            ["pol_worship_tax_desc"] = new[] { "Places of worship pay property tax, which brings in money when they are well attended. Believers in the area are a little unhappier.", "Gotteshäuser zahlen Grundsteuer, was bei gut besuchten Häusern Geld bringt. Gläubige in der Gegend sind etwas unzufriedener.", "Los lugares de culto pagan impuesto sobre la propiedad, lo que da ingresos cuando tienen muchos fieles. Los creyentes de la zona están algo menos contentos.", "Les lieux de culte paient la taxe foncière, ce qui rapporte quand ils sont bien fréquentés. Les croyants du secteur sont un peu moins heureux.", "예배 장소도 재산세를 내요. 사람이 많이 오는 곳일수록 수입이 짭짤하지만, 동네 신자들은 좀 서운해해요.", "Miejsca kultu płacą podatek od nieruchomości, co daje dochód, gdy są dobrze odwiedzane. Wierni w okolicy są nieco mniej zadowoleni.", "Locais de culto pagam imposto predial, o que rende quando são bem frequentados. Os fiéis da região ficam um pouco menos felizes.", "Места поклонения платят налог на имущество — это приносит доход, если их хорошо посещают. Верующие поблизости немного менее довольны.", "礼拜场所需缴纳房产税，信众多时能带来收入。附近的信徒会有点不满。" },
            ["pol_worship_tax_bill"] = new[] { "end the tax exemption for places of worship", "die Steuerbefreiung für Gotteshäuser abschaffen", "acabar con la exención fiscal de los lugares de culto", "supprimer l'exonération fiscale des lieux de culte", "예배 장소 면세 폐지", "znieść zwolnienie podatkowe miejsc kultu", "acabar com a isenção fiscal dos locais de culto", "отменить налоговые льготы для мест поклонения", "取消礼拜场所的免税待遇" },
            ["pol_outreach"] = new[] { "Community Outreach", "Gemeindearbeit", "Ayuda comunitaria", "Action communautaire", "동네 이웃 돕기", "Pomoc społeczna", "Ação comunitária", "Помощь общине", "社区外展" },
            ["pol_outreach_desc"] = new[] { "Places of worship look after unemployed residents nearby, which cheers them up. Raises the upkeep of places of worship by 40%.", "Gotteshäuser kümmern sich um Arbeitslose in der Nähe, was ihre Stimmung hebt. Erhöht den Unterhalt der Gotteshäuser um 40 %.", "Los lugares de culto atienden a los desempleados cercanos, lo que les levanta el ánimo. Aumenta el mantenimiento de los lugares de culto un 40 %.", "Les lieux de culte s'occupent des chômeurs à proximité, ce qui leur remonte le moral. Augmente l'entretien des lieux de culte de 40 %.", "예배 장소가 동네 실직자들을 챙겨 줘서 기분이 좀 나아져요. 대신 예배 장소 유지비가 40% 올라요.", "Miejsca kultu opiekują się bezrobotnymi w okolicy, co poprawia im nastrój. Zwiększa utrzymanie miejsc kultu o 40%.", "Os locais de culto cuidam dos desempregados próximos, o que melhora o ânimo deles. Aumenta a manutenção dos locais de culto em 40%.", "Места поклонения заботятся о безработных поблизости, и тем становится легче. Содержание мест поклонения растёт на 40%.", "礼拜场所会照顾附近的失业居民，让他们心情好一些。礼拜场所的维护费增加40%。" },
            ["pol_outreach_bill"] = new[] { "fund Community Outreach", "Gemeindearbeit finanzieren", "financiar la Ayuda comunitaria", "financer l'Action communautaire", "동네 이웃 돕기 지원", "sfinansować Pomoc społeczną", "financiar a Ação comunitária", "финансировать Помощь общине", "资助社区外展" },
            ["pol_interfaith"] = new[] { "Interfaith Facilities", "Interreligiöse Einrichtungen", "Instalaciones interreligiosas", "Lieux interconfessionnels", "종교 간 공동 시설", "Obiekty międzywyznaniowe", "Instalações inter-religiosas", "Межконфессиональные здания", "跨宗教设施" },
            ["pol_interfaith_desc"] = new[] { "Places of worship set to Interfaith serve every congregation, though less well than a congregation's own.", "Auf Interreligiös gestellte Gotteshäuser dienen allen Gemeinden, wenn auch schlechter als eigene.", "Los lugares de culto configurados como interreligiosos atienden a todas las congregaciones, aunque peor que los propios.", "Les lieux de culte réglés sur Interconfessionnel servent toutes les congrégations, moins bien que les leurs.", "종교 간 공동으로 정한 예배 장소는 모든 공동체가 함께 써요. 다만 자기네 전용 시설만큼 좋진 않아요.", "Miejsca kultu ustawione jako międzywyznaniowe służą wszystkim wspólnotom, choć gorzej niż własne.", "Locais de culto definidos como inter-religiosos atendem todas as congregações, embora pior que os próprios.", "Места поклонения с режимом «Межконфессиональное» служат всем общинам, но хуже, чем собственные.", "设为跨宗教的礼拜场所服务所有宗教团体，但效果不如各自的场所。" },
            ["pol_interfaith_bill"] = new[] { "allow Interfaith Facilities", "Interreligiöse Einrichtungen erlauben", "permitir Instalaciones interreligiosas", "autoriser les Lieux interconfessionnels", "종교 간 공동 시설 허용", "dopuścić Obiekty międzywyznaniowe", "permitir Instalações inter-religiosas", "разрешить Межконфессиональные здания", "允许跨宗教设施" },
        };

        internal static int Index
        {
            get
            {
                try { int i = Array.IndexOf(Codes, LocaleManager.cultureInfo.TwoLetterISOLanguageName); return i < 0 ? 0 : i; }
                catch { return 0; }
            }
        }

        internal static string T(string id) => Text.TryGetValue(id, out string[] t) ? Pick(t) : id;

        internal static string Game(string id, string key, string fallback)
        {
            try { if (Locale.Exists(id, key)) return Locale.Get(id, key); }
            catch { }
            return fallback;
        }

        internal static string TaxesTooHigh() => Game("NOTIFICATION_NORMAL", "TaxesTooHigh", "Taxes too high!").Trim().TrimStart('¡').TrimEnd('!', '！', ' ', '\u00a0');

        internal static string Pick(string[] t) => Pick(t, Index);

        static string Pick(string[] t, int i)
        {
            string s = t[i];
            return string.IsNullOrEmpty(s) ? t[0] : s;
        }

        internal static string T(string id, string code)
        {
            int i = Array.IndexOf(Codes, code);
            return Text.TryGetValue(id, out string[] t) ? Pick(t, i < 0 ? 0 : i) : id;
        }

        internal static string F(string template, params object[] args) => string.Format(Plural.Instance, template, args);

        internal static int Form(long n, int language)
        {
            long a = Math.Abs(n), ten = a % 10, hundred = a % 100;
            switch (language)
            {
                case 3: return a <= 1 ? 0 : 1;
                case 4:
                case 8: return 0;
                case 5: return a == 1 ? 0 : ten >= 2 && ten <= 4 && (hundred < 12 || hundred > 14) ? 1 : 2;
                case 7: return ten == 1 && hundred != 11 ? 0 : ten >= 2 && ten <= 4 && (hundred < 12 || hundred > 14) ? 1 : 2;
                default: return a == 1 ? 0 : 1;
            }
        }

        sealed class Plural : IFormatProvider, ICustomFormatter
        {
            internal static readonly Plural Instance = new Plural();

            public object GetFormat(Type type) => type == typeof(ICustomFormatter) ? this : null;

            public string Format(string format, object arg, IFormatProvider provider)
            {
                if (format != null && format.IndexOf('|') >= 0 && arg is IConvertible c)
                {
                    long n = c.ToInt64(System.Globalization.CultureInfo.InvariantCulture);
                    string[] forms = format.Split('|');
                    return forms[Math.Min(Form(n, Index), forms.Length - 1)].Replace("#", n.ToString("N0"));
                }
                if (arg is IFormattable f) return f.ToString(format, System.Globalization.CultureInfo.CurrentCulture);
                return arg == null ? string.Empty : arg.ToString();
            }
        }
    }

    static class Chirps
    {
        internal static readonly Dictionary<string, string[][]> Text = new Dictionary<string, string[][]>
        {
            ["Evicted"] = new[]
            {
                new[] { "Got the #eviction notice last month, now the locks are changed. Where are we supposed to go, @mayor?", "Letzten Monat kam die #Kündigung, jetzt sind die Schlösser ausgetauscht. Wo sollen wir denn hin, @Bürgermeister?", "El mes pasado nos llegó el aviso de #desahucio y ahora han cambiado la cerradura. ¿Adónde se supone que vamos, @alcalde?", "Le préavis d'#expulsion est arrivé le mois dernier, et maintenant les serrures sont changées. On est censés aller où, @maire ?", "지난달에 #퇴거 통보받았는데 오늘 자물쇠가 바뀌어 있음. 우리 이제 어디로 가라는 거예요, @시장님?", "W zeszłym miesiącu przyszło wypowiedzenie, a dziś wymienili zamki. #Eksmisja i tyle. Gdzie mamy się podziać, @burmistrz?", "O aviso de #despejo chegou mês passado e agora trocaram as fechaduras. Pra onde a gente vai, @prefeito?", "В прошлом месяце пришло уведомление о #выселении, а теперь сменили замки. И куда нам идти, @mayor?", "上个月收到#驱逐通知，今天门锁就换了。@市长 我们该去哪儿？" },
                new[] { "My landlord says the rent \"reflects the neighborhood.\" I reflect a cardboard box now. #housingcrisis", "Mein Vermieter sagt, die Miete \"spiegelt die Nachbarschaft wider\". Ich spiegele jetzt einen Pappkarton wider. #wohnungsnot", "Mi casero dice que el alquiler \"refleja el barrio\". Yo ahora reflejo una caja de cartón. #crisisdevivienda", "Mon proprio dit que le loyer \"reflète le quartier\". Moi, je reflète un carton maintenant. #crisedulogement", "집주인 왈, 월세는 \"동네 수준을 반영한 것\"이라고. 나는 이제 박스 한 장 수준을 반영 중임. #주거위기", "Właściciel mówi, że czynsz \"odzwierciedla okolicę\". Ja odzwierciedlam teraz karton. #kryzysmieszkaniowy", "Meu senhorio diz que o aluguel \"reflete o bairro\". Eu agora reflito uma caixa de papelão. #crisedemoradia", "Арендодатель говорит, что цена \"отражает район\". А я теперь отражаю картонную коробку. #жилищныйкризис", "房东说租金\"反映了这个街区的档次\"。我现在只反映得了一个纸箱。#住房危机" }
            },
            ["LeftCity"] = new[]
            {
                new[] { "Couldn't find a single place we could afford. We're moving back in with my parents in the next town over.", "Keine einzige bezahlbare Wohnung gefunden. Wir ziehen wieder zu meinen Eltern in die Nachbarstadt.", "No encontramos ni un solo sitio que pudiéramos pagar. Volvemos a casa de mis padres, en el pueblo de al lado.", "Impossible de trouver un seul logement abordable. On retourne vivre chez mes parents, dans la ville d'à côté.", "우리가 감당할 수 있는 집이 단 한 곳도 없었음. 옆 동네 부모님 집으로 다시 들어감.", "Nie znaleźliśmy ani jednego mieszkania, na które byłoby nas stać. Wracamy do moich rodziców w sąsiednim mieście.", "Não achamos nenhum lugar que coubesse no bolso. Vamos voltar a morar com meus pais na cidade vizinha.", "Не нашли ни одного жилья по карману. Переезжаем обратно к моим родителям в соседний город.", "找不到一个租得起的地方。我们要搬回隔壁镇的爸妈家了。" },
                new[] { "This city is lovely if you can pay for it. We can't. #movingout", "Diese Stadt ist toll, wenn man sie sich leisten kann. Wir können es nicht. #wegziehen", "Esta ciudad es preciosa si puedes pagarla. Nosotros no podemos. #nosvamos", "Cette ville est charmante, si on a les moyens. Nous, non. #ondéménage", "이 도시 참 좋음. 돈만 있으면. 우리는 없음. #이사감", "To miasto jest cudowne, jeśli cię na nie stać. Nas nie stać. #wyprowadzka", "Essa cidade é linda, se você puder pagar. A gente não pode. #mudança", "Этот город прекрасен, если он вам по карману. Нам — нет. #переезд", "这座城市很美，前提是你付得起。我们付不起。#搬走了" }
            },
            ["Settled"] = new[]
            {
                new[] { "Tent's up, neighbors are nice, the park has a fountain. It's not #home, but it's what we've got.", "Das Zelt steht, die Nachbarn sind nett, im Park gibt's einen Brunnen. Kein #Zuhause, aber das, was wir haben.", "La tienda está montada, los vecinos son majos y el parque tiene una fuente. No es un #hogar, pero es lo que hay.", "La tente est montée, les voisins sont sympas, le parc a une fontaine. Ce n'est pas un #foyer, mais c'est ce qu'on a.", "텐트 쳤고, 이웃들 착하고, 공원엔 분수도 있음. #집은 아니지만 이게 우리가 가진 전부임.", "Namiot rozbity, sąsiedzi mili, w parku jest fontanna. To nie #dom, ale tyle mamy.", "Barraca montada, vizinhos legais, o parque tem chafariz. Não é um #lar, mas é o que temos.", "Палатка стоит, соседи хорошие, в парке есть фонтан. Не #дом, но что есть, то есть.", "帐篷搭好了，邻居人不错，公园里还有喷泉。这不是#家，但这是我们仅有的。" },
                new[] { "Not leaving. This is my city too. #homeless", "Ich gehe nicht weg. Das ist auch meine Stadt. #obdachlos", "No me voy. Esta también es mi ciudad. #sinhogar", "Je ne pars pas. C'est aussi ma ville. #sansabri", "안 떠남. 여기도 내 도시임. #노숙", "Nie wyjeżdżam. To też moje miasto. #bezdomni", "Não vou embora. Essa cidade também é minha. #semteto", "Никуда не уеду. Это и мой город тоже. #бездомные", "我不走。这也是我的城市。#无家可归" }
            },
            ["Rehoused"] = new[]
            {
                new[] { "KEYS! We have keys again! #newhome #freshstart", "SCHLÜSSEL! Wir haben wieder Schlüssel! #neuesZuhause #neuanfang", "¡LLAVES! ¡Volvemos a tener llaves! #nuevohogar #empezardecero", "DES CLÉS ! On a de nouveau des clés ! #nouveauchezsoi #nouveaudépart", "열쇠!!! 우리한테 다시 열쇠가 생겼음! #새집 #새출발", "KLUCZE! Znowu mamy klucze! #nowydom #nowypoczątek", "CHAVES! Temos chaves de novo! #casanova #recomeço", "КЛЮЧИ! У нас снова есть ключи! #новыйдом #новаяжизнь", "钥匙！我们又有钥匙了！#新家 #重新开始" },
                new[] { "Six weeks on the street and tonight we sleep in a real #bed. I might actually cry.", "Sechs Wochen auf der Straße, und heute Nacht schlafen wir in einem richtigen #Bett. Ich könnte heulen.", "Seis semanas en la calle y esta noche dormimos en una #cama de verdad. Creo que voy a llorar.", "Six semaines à la rue, et ce soir on dort dans un vrai #lit. Je crois que je vais pleurer.", "길에서 6주 보내고 오늘 밤 드디어 진짜 #침대에서 잠. 눈물 날 것 같음.", "Sześć tygodni na ulicy, a dziś śpimy w prawdziwym #łóżku. Chyba się popłaczę.", "Seis semanas na rua e hoje a gente dorme numa #cama de verdade. Acho que vou chorar.", "Шесть недель на улице, а сегодня ночуем в настоящей #кровати. Кажется, сейчас расплачусь.", "流落街头六个星期，今晚终于能睡在真正的#床上了。我快哭了。" }
            },
            ["PublicHousing"] = new[]
            {
                new[] { "Moved into the new #publichousing block today. Rent we can actually pay!", "Heute in den neuen #Sozialbau gezogen. Eine Miete, die wir tatsächlich zahlen können!", "Hoy nos hemos mudado al nuevo bloque de #viviendapública. ¡Un alquiler que podemos pagar!", "Emménagé aujourd'hui dans le nouveau #logementsocial. Un loyer qu'on peut vraiment payer !", "오늘 새 #공공주택으로 이사함. 진짜로 낼 수 있는 월세라니!", "Dziś wprowadziliśmy się do nowego bloku #komunalnego. Czynsz, który naprawdę damy radę płacić!", "Mudamos hoje pro novo conjunto de #habitaçãopopular. Um aluguel que dá pra pagar!", "Сегодня переехали в новый #социальныйдом. Аренда, которую мы реально можем платить!", "今天搬进了新的#公共住房。终于有了付得起的房租！" },
                new[] { "Thanks @mayor, the public housing is small but it's ours. #affordable", "Danke, @Bürgermeister, die Sozialwohnung ist klein, aber sie gehört uns. #bezahlbar", "Gracias, @alcalde: la vivienda pública es pequeña, pero es nuestra. #asequible", "Merci @maire, le logement social est petit, mais c'est le nôtre. #abordable", "@시장님 감사해요. 공공주택이 좁긴 해도 우리 집임. #내집", "Dzięki, @burmistrz, mieszkanie komunalne jest małe, ale nasze. #przystępne", "Valeu, @prefeito, a moradia popular é pequena, mas é nossa. #acessível", "Спасибо, @mayor, социальная квартира маленькая, но наша. #доступноежильё", "谢谢 @市长，公共住房虽然小，但是是我们的。#住得起" }
            },
            ["Died"] = new[]
            {
                new[] { "We lost someone from the park last night. Nobody should die outside in a city this rich.", "Letzte Nacht haben wir jemanden aus dem Park verloren. Niemand sollte in einer so reichen Stadt draußen sterben.", "Anoche perdimos a alguien del parque. Nadie debería morir en la calle en una ciudad tan rica.", "On a perdu quelqu'un du parc cette nuit. Personne ne devrait mourir dehors dans une ville aussi riche.", "어젯밤 공원에서 지내던 분이 세상을 떠났음. 이렇게 부자 도시에서 밖에서 죽는 사람이 있어선 안 됨.", "Zeszłej nocy straciliśmy kogoś z parku. Nikt nie powinien umierać na ulicy w tak bogatym mieście.", "Perdemos alguém do parque ontem à noite. Ninguém deveria morrer na rua numa cidade tão rica.", "Прошлой ночью мы потеряли человека из парка. Никто не должен умирать на улице в таком богатом городе.", "昨晚公园里的一位朋友走了。在这么富裕的城市里，不该有人死在街头。" },
                new[] { "Another life lost on our streets. When will @mayor treat homelessness as an emergency? #housingisahumanright", "Wieder ein Leben auf unseren Straßen verloren. Wann behandelt @Bürgermeister Obdachlosigkeit endlich als Notfall? #wohnenistmenschenrecht", "Otra vida perdida en nuestras calles. ¿Cuándo tratará el @alcalde el sinhogarismo como una emergencia? #laviviendaesunderecho", "Encore une vie perdue dans nos rues. Quand le @maire traitera-t-il le sans-abrisme comme une urgence ? #lelogementestundroit", "또 한 명이 거리에서 목숨을 잃었음. @시장님은 언제쯤 노숙 문제를 비상사태로 받아들일 건가요? #주거는인권", "Kolejne życie stracone na naszych ulicach. Kiedy @burmistrz uzna bezdomność za sytuację kryzysową? #mieszkanietoprawo", "Mais uma vida perdida nas nossas ruas. Quando o @prefeito vai tratar a falta de moradia como emergência? #moradiaédireito", "Ещё одна жизнь оборвалась на наших улицах. Когда @mayor признает бездомность чрезвычайной ситуацией? #жильёэтоправо", "又一条生命消逝在街头。@市长 什么时候才会把无家可归当成紧急问题？#住房是人权" }
            },
            ["Compensation"] = new[]
            {
                new[] { "@cityhall pays ₡{0} to {1:# displaced household|# displaced households} after demolition. Residents say no amount replaces a home. #displacement", "@Rathaus zahlt nach dem Abriss ₡{0} an {1:# vertriebenen Haushalt|# vertriebene Haushalte}. Die Bewohner sagen, kein Geld ersetzt ein Zuhause. #verdrängung", "El @ayuntamiento paga ₡{0} a {1:# hogar desplazado|# hogares desplazados} tras la demolición. Los vecinos dicen que ninguna cifra sustituye a un hogar. #desplazados", "La @mairie verse ₡{0} à {1:# foyer déplacé|# foyers déplacés} après la démolition. Les habitants disent qu'aucune somme ne remplace un foyer. #déplacés", "@시청, 철거로 쫓겨난 {1}가구에 ₡{0} 지급. 주민들은 돈으로 집을 대신할 순 없다고 함. #강제철거", "@ratusz wypłaca ₡{0} dla {1:# wysiedlonego gospodarstwa|# wysiedlonych gospodarstw|# wysiedlonych gospodarstw} po wyburzeniu. Mieszkańcy mówią, że żadna kwota nie zastąpi domu. #wysiedlenia", "A @prefeitura paga ₡{0} a {1:# família desalojada|# famílias desalojadas} após a demolição. Moradores dizem que nenhum valor substitui um lar. #desalojados", "@мэрия выплатила ₡{0} {1:# семье, выселенной|# семьям, выселенным|# семьям, выселенным} из-за сноса. Жители говорят, что никакие деньги не заменят дом. #снос", "@市政厅 拆迁后向 {1} 户被迫搬离的家庭支付了 ₡{0}。居民们说，多少钱都换不回一个家。#拆迁" },
                new[] { "{1:# family|# families} bought out for ₡{0}. My #grandmother lived in that building for forty years.", "{1:# Familie|# Familien} für ₡{0} rausgekauft. Meine #Oma hat vierzig Jahre in dem Haus gewohnt.", "{1:# familia comprada|# familias compradas} por ₡{0}. Mi #abuela vivió cuarenta años en ese edificio.", "{1:# famille rachetée|# familles rachetées} pour ₡{0}. Ma #grandmère a vécu quarante ans dans cet immeuble.", "{1}가구가 ₡{0}에 쫓겨남. 우리 #할머니는 그 건물에서 40년을 사셨는데.", "{1:# rodzina spłacona|# rodziny spłacone|# rodzin spłaconych} za ₡{0}. Moja #babcia mieszkała w tym budynku czterdzieści lat.", "{1:# família comprada|# famílias compradas} por ₡{0}. Minha #avó morou quarenta anos naquele prédio.", "{1:# семью выкупили|# семьи выкупили|# семей выкупили} за ₡{0}. Моя #бабушка прожила в этом доме сорок лет.", "{1} 户人家被 ₡{0} 买断。我#奶奶在那栋楼里住了四十年。" }
            },
            ["Protest"] = new[]
            {
                new[] { "Hands off our homes! Join us outside city hall at noon. #protest #nodemolition", "Hände weg von unseren Wohnungen! Kommt mittags mit uns vors Rathaus. #protest #keinabriss", "¡Fuera las manos de nuestras casas! Únete a nosotros frente al ayuntamiento a mediodía. #protesta #noalademolición", "Touchez pas à nos logements ! Rejoignez-nous devant la mairie à midi. #manif #nonàladémolition", "우리 집에서 손 떼라! 정오에 시청 앞으로 모여 주세요. #시위 #철거반대", "Ręce precz od naszych domów! Dołącz do nas w południe przed ratuszem. #protest #niedlawyburzeń", "Tirem as mãos das nossas casas! Venha com a gente pra frente da prefeitura ao meio-dia. #protesto #nãoàdemolição", "Руки прочь от наших домов! Присоединяйтесь к нам в полдень у мэрии. #протест #нетсносу", "别碰我们的家！中午到市政厅门口和我们一起抗议。#抗议 #反对拆迁" },
                new[] { "Bulldozers came for our block today. We're not going quietly.", "Heute kamen die Bulldozer in unseren Block. Wir gehen nicht kampflos.", "Hoy han venido las excavadoras a nuestra manzana. No nos vamos a ir en silencio.", "Les bulldozers sont venus pour notre pâté de maisons aujourd'hui. On ne partira pas sans rien dire.", "오늘 우리 동네에 불도저가 들이닥침. 그냥 조용히 나가진 않을 거임.", "Dziś po nasz kwartał przyjechały buldożery. Nie odejdziemy po cichu.", "As escavadeiras vieram atrás do nosso quarteirão hoje. Não vamos sair calados.", "Сегодня бульдозеры пришли за нашим кварталом. Мы не уйдём молча.", "推土机今天开进了我们这片街区。我们不会默默离开。" }
            },
            ["Unaffordable"] = new[]
            {
                new[] { "Found my dream #apartment downtown. The rent is my entire salary.", "Meine #Traumwohnung in der Innenstadt gefunden. Die Miete ist mein komplettes Gehalt.", "He encontrado el #piso de mis sueños en el centro. El alquiler es mi sueldo entero.", "Trouvé l'#appartement de mes rêves en centre-ville. Le loyer, c'est tout mon salaire.", "도심에서 꿈의 #집을 찾았음. 월세가 내 월급 전부임.", "Znalazłem #mieszkanie marzeń w centrum. Czynsz to cała moja pensja.", "Achei o #apê dos sonhos no centro. O aluguel é meu salário inteiro.", "Нашёл #квартиру мечты в центре. Аренда — вся моя зарплата.", "在市中心找到了梦想中的#公寓。房租等于我全部工资。" },
                new[] { "Looked at twelve apartments today. Every single one was out of my price range. #pricedout", "Heute zwölf Wohnungen angeschaut. Jede einzelne war zu teuer für mich. #rausgepreist", "Hoy he visto doce pisos. Ni uno solo entraba en mi presupuesto. #fueradelmercado", "Visité douze appartements aujourd'hui. Pas un seul dans mon budget. #tropcher", "오늘 집 12군데 보러 다님. 하나같이 내 예산 밖이었음. #집값미쳤다", "Obejrzałem dziś dwanaście mieszkań. Każde było poza moim budżetem. #zadrogo", "Visitei doze apartamentos hoje. Nenhum cabia no meu orçamento. #carodemais", "Посмотрел сегодня двенадцать квартир. Ни одна не по карману. #неподкарману", "今天看了十二套房子，没有一套在我的预算之内。#买不起" }
            },
            ["Organized"] = new[]
            {
                new[] { "First meeting of our new #neighborhood association tonight! Bring snacks and grievances.", "Heute Abend erstes Treffen unseres neuen #Nachbarschaftsvereins! Bringt Snacks und Beschwerden mit.", "¡Esta noche, primera reunión de nuestra nueva #asociaciónvecinal! Traed algo de picar y vuestras quejas.", "Première réunion de notre nouvelle #associationdequartier ce soir ! Apportez des snacks et vos doléances.", "오늘 밤 우리 #주민협의회 첫 모임! 간식이랑 불만거리 챙겨 오세요.", "Dziś wieczorem pierwsze spotkanie naszego nowego #stowarzyszenia mieszkańców! Przynieście przekąski i żale.", "Primeira reunião da nossa nova #associaçãodemoradores hoje à noite! Tragam petiscos e reclamações.", "Сегодня вечером первое собрание нашего нового #соседскогообъединения! Берите с собой закуски и претензии.", "今晚是我们新#居民协会的第一次会议！带上零食和牢骚来吧。" },
                new[] { "We're organizing. If @mayor won't listen to one of us, maybe they'll listen to all of us. #neighbors", "Wir organisieren uns. Wenn @Bürgermeister nicht auf einen von uns hört, dann vielleicht auf uns alle. #nachbarn", "Nos estamos organizando. Si el @alcalde no escucha a uno de nosotros, quizá nos escuche a todos. #vecinos", "On s'organise. Si le @maire n'écoute pas l'un d'entre nous, peut-être qu'il nous écoutera tous. #voisins", "우리 뭉치는 중. @시장님이 한 명 말은 안 들어도, 다 같이 말하면 듣겠지. #이웃", "Organizujemy się. Skoro @burmistrz nie słucha jednego z nas, może wysłucha nas wszystkich. #sąsiedzi", "Estamos nos organizando. Se o @prefeito não escuta um de nós, talvez escute todos nós. #vizinhos", "Мы объединяемся. Если @mayor не слушает одного из нас, может, послушает всех. #соседи", "我们组织起来了。@市长 不听一个人的，也许会听我们所有人的。#邻居" }
            },
            ["Demand"] = new[]
            {
                new[] { "Our neighborhood association has one request for @mayor: {0}. #wedemand", "Unser Nachbarschaftsverein hat eine Forderung an @Bürgermeister: {0}. #wirfordern", "Nuestra asociación vecinal tiene una petición para el @alcalde: {0}. #exigimos", "Notre association de quartier a une demande pour le @maire : {0}. #nousexigeons", "우리 주민 협의회가 @시장님께 딱 하나 요구함: {0}. #요구합니다", "Nasze stowarzyszenie mieszkańców ma do @burmistrz jedną prośbę: {0}. #żądamy", "Nossa associação de moradores tem um pedido pro @prefeito: {0}. #exigimos", "У нашего соседского объединения одно требование к @mayor: {0}. #мытребуем", "我们居民协会对 @市长 只有一个要求：{0}。#我们要求" },
                new[] { "#Petition is live! We want {0}, and we want it before the next election.", "Die #Petition läuft! Wir wollen {0}, und zwar vor der nächsten Wahl.", "¡La #petición ya está en marcha! Queremos {0}, y lo queremos antes de las próximas elecciones.", "La #pétition est en ligne ! On veut {0}, et on le veut avant les prochaines élections.", "#청원 시작! 우리는 {0}을(를) 원하고, 다음 선거 전까지 원함.", "#Petycja ruszyła! Chcemy: {0}, i to przed następnymi wyborami.", "O #abaixoassinado está no ar! Queremos {0}, e antes da próxima eleição.", "#Петиция запущена! Мы хотим {0}, и хотим до следующих выборов.", "#请愿开始了！我们要{0}，而且要在下次选举之前。" }
            },
            ["DemandMet"] = new[]
            {
                new[] { "They listened! We asked for {0} and we got it. #communitywin", "Sie haben zugehört! Wir haben {0} gefordert und es bekommen. #gemeinsamgewonnen", "¡Nos han escuchado! Pedimos {0} y lo hemos conseguido. #victoriavecinal", "Ils ont écouté ! On a demandé {0} et on l'a obtenu. #victoireduquartier", "들어줬다! 우리가 {0}을(를) 요구했는데 진짜 해 줌. #동네의승리", "Wysłuchali nas! Nasza prośba: {0} – spełniona! #wygranaspołeczności", "Eles ouviram! Pedimos {0} e conseguimos. #vitóriadacomunidade", "Нас услышали! Мы просили {0} и получили. #победасоседей", "他们听进去了！我们要{0}，真的实现了。#社区的胜利" },
                new[] { "Proof that organizing works: {0}, delivered. Thanks @mayor. #victory", "Der Beweis, dass sich Organisieren lohnt: {0}, geliefert. Danke, @Bürgermeister. #sieg", "La prueba de que organizarse funciona: {0}, conseguido. Gracias, @alcalde. #victoria", "La preuve que s'organiser marche : {0}, c'est fait. Merci @maire. #victoire", "뭉치면 된다는 증거: {0}, 해결 완료. @시장님 고마워요. #승리", "Dowód, że organizowanie się działa: {0} – załatwione. Dzięki, @burmistrz. #zwycięstwo", "Prova de que se organizar funciona: {0}, entregue. Valeu, @prefeito. #vitória", "Доказательство, что объединяться стоит: {0} — сделано. Спасибо, @mayor. #победа", "组织起来是有用的：{0}，已经兑现。谢谢 @市长。#胜利" }
            },
            ["DemandIgnored"] = new[]
            {
                new[] { "We asked for {0}. Months later, nothing. Remember this at the ballot box. #ignored", "Wir haben {0} gefordert. Monate später: nichts. Denkt daran an der Wahlurne. #ignoriert", "Pedimos {0}. Meses después, nada. Recordadlo en las urnas. #ignorados", "On a demandé {0}. Des mois plus tard, rien. Souvenez-vous-en aux urnes. #ignorés", "우리가 {0}을(를) 요구한 지 몇 달째, 아무것도 없음. 투표할 때 꼭 기억하자. #무시당함", "Nasza prośba: {0}. Minęły miesiące i nic. Pamiętajcie o tym przy urnach. #zignorowani", "Pedimos {0}. Meses depois, nada. Lembrem disso na hora de votar. #ignorados", "Мы просили {0}. Прошли месяцы — ничего. Вспомните об этом на выборах. #насигнорируют", "我们要求{0}。几个月过去了，什么都没有。投票的时候记住这件事。#被无视" },
                new[] { "@mayor had plenty of time to give us {0}. Instead, silence.", "@Bürgermeister hatte genug Zeit, uns {0} zu geben. Stattdessen: Schweigen.", "El @alcalde tuvo tiempo de sobra para darnos {0}. En su lugar, silencio.", "Le @maire avait tout le temps de nous donner {0}. À la place, le silence.", "@시장님, {0} 해 줄 시간 충분했잖아요. 근데 묵묵부답.", "@burmistrz miał mnóstwo czasu, żeby załatwić: {0}. Zamiast tego cisza.", "O @prefeito teve tempo de sobra pra nos dar {0}. Em vez disso, silêncio.", "У @mayor было полно времени, чтобы дать нам {0}. Вместо этого — тишина.", "@市长 有足够的时间给我们{0}。结果却只有沉默。" }
            },
            ["Objection"] = new[]
            {
                new[] { "Our objections added ₡{0} to that project. Maybe next time ask us first? #nimby #notinourbackyard", "Unsere Einwände haben das Projekt um ₡{0} verteuert. Vielleicht fragt ihr uns nächstes Mal vorher? #nimby #nichtvorunsererhaustür", "Nuestras alegaciones han encarecido el proyecto en ₡{0}. ¿Y si la próxima vez nos preguntáis antes? #nimby #aquíno", "Nos recours ont ajouté ₡{0} à ce projet. La prochaine fois, demandez-nous d'abord ? #nimby #pasdansmonjardin", "우리 반대 덕에 그 공사에 ₡{0} 추가됨. 다음엔 우리한테 먼저 물어보는 게 어때요? #님비 #우리동네는안돼", "Nasze sprzeciwy podniosły koszt tej inwestycji o ₡{0}. Może następnym razem najpierw nas zapytacie? #nimby #nieunas", "Nossas objeções deixaram aquela obra ₡{0} mais cara. Que tal perguntar pra gente antes da próxima vez? #nimby #aquinão", "Наши возражения прибавили к этому проекту ₡{0}. Может, в следующий раз сначала спросите нас? #nimby #тольконеунас", "因为我们的反对，那个项目多花了 ₡{0}。下次先问问我们好吗？#邻避 #别建在我家门口" },
                new[] { "Lawyers, environmental reviews, #publichearings: ₡{0} later, they're still building it.", "Anwälte, Umweltgutachten, #Anhörungen: ₡{0} später bauen sie es trotzdem.", "Abogados, estudios ambientales, #audienciaspúblicas: ₡{0} después, siguen construyéndolo.", "Avocats, études d'impact, #audiencespubliques : ₡{0} plus tard, ils le construisent quand même.", "변호사, 환경영향평가, #공청회까지: ₡{0} 날리고도 결국 짓는 중.", "Prawnicy, raporty środowiskowe, #konsultacje: ₡{0} później i tak to budują.", "Advogados, estudos ambientais, #audiênciaspúblicas: ₡{0} depois, continuam construindo.", "Юристы, экологическая экспертиза, #общественныеслушания: ₡{0} спустя всё равно строят.", "律师、环评、#听证会：花了 ₡{0}，他们还是照建不误。" }
            },
            ["StrikeStart"] = new[]
            {
                new[] { "The #rentstrike starts today. We'll pay when it's fair.", "Heute beginnt der #Mietstreik. Wir zahlen, wenn es fair ist.", "Hoy empieza la #huelgadealquileres. Pagaremos cuando sea justo.", "La #grèvedesloyers commence aujourd'hui. On paiera quand ce sera juste.", "오늘부터 #월세파업 시작. 공정해지면 그때 낼게요.", "Dziś zaczyna się #strajkczynszowy. Zapłacimy, kiedy będzie uczciwie.", "A #grevedealuguel começa hoje. A gente paga quando for justo.", "Сегодня начинается #аренднаязабастовка. Заплатим, когда будет честно.", "#罢缴房租 从今天开始。等租金公道了我们再交。" },
                new[] { "Not one more cent until rents come down. Tenants united! #rentstrike #solidarity", "Kein Cent mehr, bis die Mieten sinken. Mieter, vereint euch! #mietstreik #solidarität", "Ni un céntimo más hasta que bajen los alquileres. ¡Inquilinos unidos! #huelgadealquileres #solidaridad", "Plus un centime tant que les loyers ne baissent pas. Locataires unis ! #grèvedesloyers #solidarité", "월세 내릴 때까지 한 푼도 못 냄. 세입자여 단결하라! #월세파업 #연대", "Ani grosza więcej, dopóki czynsze nie spadną. Najemcy, łączcie się! #strajkczynszowy #solidarność", "Nem mais um centavo até o aluguel baixar. Inquilinos unidos! #grevedealuguel #solidariedade", "Ни копейки, пока аренда не снизится. Жильцы, объединяйтесь! #арендная_забастовка #солидарность", "房租不降，一分不交。租户们团结起来！#罢缴房租 #团结" }
            },
            ["StrikeEnd"] = new[]
            {
                new[] { "Rents are finally coming down, so we're ending the strike. We'll be watching. #rentstrike", "Die Mieten sinken endlich, also beenden wir den Streik. Wir behalten das im Auge. #mietstreik", "Los alquileres por fin bajan, así que terminamos la huelga. Estaremos vigilando. #huelgadealquileres", "Les loyers baissent enfin, alors on arrête la grève. On reste vigilants. #grèvedesloyers", "드디어 월세가 내려가서 파업 끝냄. 계속 지켜볼 거임. #월세파업", "Czynsze w końcu spadają, więc kończymy strajk. Będziemy patrzeć wam na ręce. #strajkczynszowy", "O aluguel finalmente está baixando, então encerramos a greve. Vamos ficar de olho. #grevedealuguel", "Аренда наконец снижается, поэтому мы прекращаем забастовку. Но будем следить. #арендная_забастовка", "房租终于降了，所以我们停止罢缴。但我们会一直盯着。#罢缴房租" },
                new[] { "Strike's over, and we won. Never underestimate a #tenantsunion, folks.", "Der Streik ist vorbei, und wir haben gewonnen. Unterschätzt nie eine #Mietergewerkschaft, Leute.", "Se acabó la huelga y hemos ganado. Nunca subestiméis a un #sindicatodeinquilinos, amigos.", "La grève est finie, et on a gagné. Ne sous-estimez jamais un #syndicatdelocataires, les amis.", "파업 끝, 우리가 이김. #세입자노조 절대 얕보지 마세요, 여러분.", "Strajk zakończony i wygraliśmy. Nigdy nie lekceważcie #związkulokatorów, ludzie.", "A greve acabou e a gente venceu. Nunca subestimem um #sindicatodeinquilinos, pessoal.", "Забастовка окончена, и мы победили. Никогда не недооценивайте #профсоюзарендаторов, друзья.", "罢缴结束了，我们赢了。大家千万别小看#租户工会。" }
            },
            ["BlockParty"] = new[]
            {
                new[] { "#Blockparty on our street! Grills are out, music's on, everyone's welcome.", "#Straßenfest bei uns! Die Grills laufen, die Musik spielt, alle sind willkommen.", "¡#Fiestadelbarrio en nuestra calle! Parrillas encendidas, música sonando y todos bienvenidos.", "#Fêtedesvoisins dans notre rue ! Les barbecues sont allumés, la musique tourne, tout le monde est bienvenu.", "우리 동네 #골목파티! 그릴 켜고, 음악 틀고, 누구나 환영.", "#Festynsąsiedzki na naszej ulicy! Grille rozpalone, muzyka gra, wszyscy mile widziani.", "#Festanarua aqui no nosso quarteirão! Churrasqueira acesa, música tocando, todo mundo é bem-vindo.", "#Праздникулицы у нас! Мангалы горят, музыка играет, все приглашены.", "我们街上的#街区派对开始啦！烤架点上了，音乐放起来了，欢迎大家来。" },
                new[] { "Met half my neighbors for the first time at today's block party. Why don't we do this every week? #goodneighbors", "Beim heutigen Straßenfest die Hälfte meiner Nachbarn zum ersten Mal getroffen. Warum machen wir das nicht jede Woche? #straßenfest", "En la fiesta de hoy he conocido a la mitad de mis vecinos por primera vez. ¿Por qué no lo hacemos cada semana? #fiestadebarrio", "J'ai rencontré la moitié de mes voisins pour la première fois à la fête de rue. Pourquoi on ne fait pas ça chaque semaine ? #fêtedequartier", "오늘 골목 파티에서 이웃 절반을 처음 만남. 이거 매주 하면 안 됨? #동네파티", "Na dzisiejszym festynie poznałem połowę sąsiadów. Czemu nie robimy tego co tydzień? #festynsąsiedzki", "Conheci metade dos meus vizinhos pela primeira vez na festa de rua de hoje. Por que a gente não faz isso toda semana? #festadebairro", "На сегодняшнем празднике впервые познакомился с половиной соседей. Почему бы не устраивать такое каждую неделю? #праздникулицы", "今天的街坊派对上第一次见到了一半的邻居。为什么不每周都办一次呢？#街坊派对" }
            },
            ["DoorstepComplaint"] = new[]
            {
                new[] { "Three tents outside the store again this morning. I feel for them, but customers just walk the other way. Can @cityhall do something?", "Heute Morgen schon wieder drei Zelte vor dem Laden. Sie tun mir leid, aber die Kundschaft macht einfach einen Bogen. Kann @Rathaus etwas tun?", "Esta mañana otra vez tres tiendas de campaña delante de la tienda. Me dan pena, pero los clientes se dan la vuelta. ¿Puede hacer algo el @ayuntamiento?", "Encore trois tentes devant la boutique ce matin. J'ai de la peine pour eux, mais les clients changent de trottoir. La @mairie peut faire quelque chose ?", "오늘 아침에도 가게 앞에 텐트가 세 개. 안됐긴 한데 손님들이 그냥 돌아가 버림. @시청 뭐라도 좀 해 줄 수 없나요?", "Dziś rano znowu trzy namioty przed sklepem. Szkoda mi ich, ale klienci po prostu omijają nas szerokim łukiem. Może @ratusz coś z tym zrobi?", "Hoje de manhã de novo três barracas na frente da loja. Fico com pena, mas os clientes simplesmente desviam. A @prefeitura pode fazer alguma coisa?", "Сегодня утром у магазина снова три палатки. Мне их жаль, но покупатели просто обходят нас стороной. @мэрия, можно что-то сделать?", "今天早上店门口又多了三顶帐篷。我同情他们，可顾客都绕道走了。@市政厅 能不能想想办法？" },
                new[] { "Our #doorstep has turned into someone's bedroom. It's not their fault, but the building manager gets calls every single day.", "Unsere #Haustür ist jetzt jemandes Schlafzimmer. Die Leute können nichts dafür, aber die Hausverwaltung bekommt jeden Tag Anrufe.", "Nuestro #portal se ha convertido en el dormitorio de alguien. No es culpa suya, pero el administrador recibe llamadas todos los días.", "Notre #pasdeporte est devenu la chambre de quelqu'un. Ce n'est pas leur faute, mais le gardien reçoit des appels tous les jours.", "우리 건물 #현관이 누군가의 침실이 됐음. 그 사람들 잘못은 아닌데 관리인한테 매일 민원 전화가 옴.", "Nasza #klatka schodowa stała się czyjąś sypialnią. To nie ich wina, ale administrator codziennie odbiera telefony.", "A #portaria do prédio virou o quarto de alguém. Não é culpa deles, mas o síndico recebe ligação todo santo dia.", "Наш #подъезд превратился в чью-то спальню. Они не виноваты, но управляющему звонят каждый день.", "我们楼的#门口成了别人的卧室。这不怪他们，可物业每天都接到投诉电话。" }
            },
            ["DoorstepLeaving"] = new[]
            {
                new[] { "We're closing up and moving out. Nobody is helping the people camped outside, and nobody is helping us either. #givingup", "Wir schließen und ziehen weg. Niemand hilft den Leuten, die draußen campieren, und uns hilft auch keiner. #aufgegeben", "Cerramos y nos vamos. Nadie ayuda a la gente que acampa fuera, y a nosotros tampoco. #nosrendimos", "On ferme et on s'en va. Personne n'aide les gens qui campent dehors, et personne ne nous aide non plus. #onabandonne", "우리 문 닫고 떠남. 밖에서 지내는 사람들도 아무도 안 도와주고, 우리도 아무도 안 도와줌. #포기", "Zamykamy i się wyprowadzamy. Nikt nie pomaga ludziom koczującym przed budynkiem i nikt nie pomaga nam. #poddajemysię", "Vamos fechar e sair daqui. Ninguém ajuda o pessoal acampado lá fora, e ninguém ajuda a gente também. #desistimos", "Закрываемся и съезжаем. Никто не помогает людям, которые ночуют снаружи, и нам тоже никто не помогает. #сдаёмся", "我们要关门搬走了。没人帮外面露宿的人，也没人帮我们。#放弃了" },
                new[] { "The #encampment out front keeps growing and the landlord says the building isn't worth fixing anymore. Packing boxes tonight.", "Das #Zeltlager vor dem Haus wird immer größer, und der Vermieter sagt, das Gebäude lohnt sich nicht mehr. Heute Abend packen wir Kisten.", "El #campamento de enfrente no para de crecer y el casero dice que ya no vale la pena arreglar el edificio. Esta noche empaquetamos.", "Le #campement devant l'immeuble ne cesse de grandir et le proprio dit que le bâtiment ne vaut plus la peine d'être réparé. Cartons ce soir.", "건물 앞 #텐트촌이 계속 커지는데 집주인은 이제 건물 손볼 가치도 없대. 오늘 밤 짐 쌈.", "#Obozowisko przed budynkiem ciągle rośnie, a właściciel mówi, że nie opłaca się już remontować. Dziś wieczorem pakujemy kartony.", "O #acampamento aqui na frente só cresce e o dono diz que não vale mais a pena consertar o prédio. Hoje à noite a gente encaixota tudo.", "#Лагерь у входа всё растёт, а хозяин говорит, что дом больше не стоит ремонтировать. Вечером пакуем коробки.", "门前的#帐篷营地越来越大，房东说这栋楼已经不值得修了。今晚打包。" }
            },
            ["Encampment"] = new[]
            {
                new[] { "There's a whole little #tentcity on our street now. Some of them have been here for months.", "In unserer Straße gibt es jetzt eine richtige kleine #Zeltstadt. Manche sind schon seit Monaten hier.", "Ahora hay toda una pequeña #ciudaddetiendas en nuestra calle. Algunos llevan aquí meses.", "Il y a maintenant une vraie petite #villedetentes dans notre rue. Certains sont là depuis des mois.", "우리 거리에 이제 작은 #텐트촌이 생겼음. 몇 달째 있는 사람들도 있음.", "Na naszej ulicy jest teraz całe małe #miasteczkonamiotowe. Niektórzy są tu od miesięcy.", "Agora tem uma pequena #cidadedebarracas inteira na nossa rua. Tem gente aqui há meses.", "На нашей улице теперь целый маленький #палаточныйгородок. Некоторые живут тут уже месяцами.", "我们街上现在有一整片小#帐篷城了。有些人已经在这儿住了好几个月。" },
                new[] { "Once one person found a safe spot, others followed. That's how our block became the city's unofficial shelter.", "Als einer einen sicheren Platz gefunden hatte, kamen andere nach. So wurde unser Block zur inoffiziellen Notunterkunft der Stadt.", "En cuanto una persona encontró un sitio seguro, otras la siguieron. Así es como nuestra manzana se convirtió en el albergue extraoficial de la ciudad.", "Dès qu'une personne a trouvé un coin sûr, d'autres ont suivi. C'est comme ça que notre quartier est devenu le refuge officieux de la ville.", "한 사람이 안전한 자리를 찾으니까 다른 사람들도 따라옴. 그렇게 우리 동네가 도시의 비공식 쉼터가 됐음.", "Gdy jedna osoba znalazła bezpieczne miejsce, inni poszli za nią. Tak nasz kwartał stał się nieoficjalnym schroniskiem miasta.", "Quando uma pessoa achou um cantinho seguro, os outros vieram atrás. Foi assim que nosso quarteirão virou o abrigo não oficial da cidade.", "Стоило одному найти безопасное место, за ним потянулись другие. Так наш квартал стал неофициальным приютом города.", "一个人找到了安全的落脚处，其他人就跟着来了。我们这片街区就这样成了城里的非官方收容所。" }
            },
            ["Sweep"] = new[]
            {
                new[] { "Police cleared the camp outside our building last night. First quiet morning in months. #cleanstreets", "Die Polizei hat letzte Nacht das Lager vor unserem Haus geräumt. Der erste ruhige Morgen seit Monaten. #sauberestraßen", "Anoche la policía desalojó el campamento de delante de nuestro edificio. La primera mañana tranquila en meses. #calleslimpias", "La police a démantelé le campement devant notre immeuble cette nuit. Premier matin calme depuis des mois. #ruespropres", "어젯밤 경찰이 우리 건물 앞 텐트촌을 싹 치웠음. 몇 달 만에 처음으로 조용한 아침. #깨끗한거리", "Policja zlikwidowała w nocy obozowisko przed naszym blokiem. Pierwszy spokojny poranek od miesięcy. #czysteulice", "A polícia desmontou o acampamento na frente do nosso prédio ontem à noite. Primeira manhã tranquila em meses. #ruaslimpas", "Ночью полиция разогнала лагерь у нашего дома. Первое спокойное утро за несколько месяцев. #чистыеулицы", "昨晚警察清走了我们楼前的营地。几个月来第一个安静的早晨。#街道清净了" },
                new[] { "They arrested a man for #sleeping in his car. Now he has a record, no job and still no home. How is that a solution, @mayor?", "Sie haben einen Mann verhaftet, weil er in seinem Auto #geschlafen hat. Jetzt hat er eine Vorstrafe, keinen Job und immer noch keine Wohnung. Was soll das lösen, @Bürgermeister?", "Han detenido a un hombre por #dormir en su coche. Ahora tiene antecedentes, ni trabajo ni casa. ¿Eso es una solución, @alcalde?", "Ils ont arrêté un homme parce qu'il #dormait dans sa voiture. Maintenant il a un casier, pas de travail et toujours pas de logement. C'est ça la solution, @maire ?", "자기 차에서 #잠잤다고 사람을 잡아감. 이제 전과 생기고, 일자리도 없고, 집은 여전히 없음. 이게 해결책이에요, @시장님?", "Aresztowali faceta za #spanie we własnym samochodzie. Teraz ma kartotekę, nie ma pracy i dalej nie ma domu. To ma być rozwiązanie, @burmistrz?", "Prenderam um homem por #dormir no próprio carro. Agora ele tem ficha, nem emprego e continua sem casa. Isso é solução, @prefeito?", "Человека арестовали за то, что он #спал в своей машине. Теперь у него судимость, нет работы и по-прежнему нет жилья. Это решение, @mayor?", "有人因为在自己车里#睡觉 被抓了。现在他有了案底，没工作，还是没地方住。@市长 这算什么解决办法？" }
            },
            ["Record"] = new[]
            {
                new[] { "Got out of jail a month ago. Every job application ends the moment they see the record.", "Bin vor einem Monat aus dem Gefängnis gekommen. Jede Bewerbung ist vorbei, sobald sie die Vorstrafe sehen.", "Salí de la cárcel hace un mes. Todas las solicitudes de trabajo terminan en cuanto ven los antecedentes.", "Sorti de prison il y a un mois. Chaque candidature s'arrête dès qu'ils voient le casier.", "한 달 전에 출소했음. 전과 보는 순간 모든 지원서가 끝남.", "Wyszedłem z więzienia miesiąc temu. Każda rekrutacja kończy się, gdy tylko zobaczą kartotekę.", "Saí da prisão faz um mês. Toda entrevista de emprego acaba na hora que eles veem a ficha.", "Вышел из тюрьмы месяц назад. Любое собеседование заканчивается, как только видят судимость.", "一个月前出狱了。每次求职，人家一看到案底就没下文了。" },
                new[] { "Served my time, but my #record keeps punishing me. Twelve applications, twelve rejections.", "Ich habe meine Strafe abgesessen, aber meine #Vorstrafe bestraft mich weiter. Zwölf Bewerbungen, zwölf Absagen.", "Ya cumplí mi condena, pero mis #antecedentes me siguen castigando. Doce solicitudes, doce rechazos.", "J'ai purgé ma peine, mais mon #casier continue de me punir. Douze candidatures, douze refus.", "죗값은 다 치렀는데 #전과가 계속 발목을 잡음. 열두 군데 지원, 열두 번 탈락.", "Odsiedziałem swoje, ale #kartoteka dalej mnie karze. Dwanaście podań, dwanaście odmów.", "Já cumpri minha pena, mas minha #ficha continua me punindo. Doze currículos, doze recusas.", "Срок я отсидел, но #судимость продолжает меня наказывать. Двенадцать заявок — двенадцать отказов.", "刑期已经服完了，可#案底还在继续惩罚我。投了十二份简历，被拒了十二次。" }
            },
            ["Rubble"] = new[]
            {
                new[] { "They sent the #bulldozers before anyone searched the rubble. My neighbors were still in there.", "Sie haben die #Bulldozer geschickt, bevor jemand die Trümmer durchsucht hat. Meine Nachbarn waren noch da drin.", "Mandaron las #excavadoras antes de que nadie buscara entre los escombros. Mis vecinos seguían ahí dentro.", "Ils ont envoyé les #bulldozers avant que quiconque fouille les décombres. Mes voisins étaient encore dedans.", "잔해 수색도 안 하고 #불도저부터 보냈음. 우리 이웃들이 아직 안에 있었는데.", "Wysłali #buldożery, zanim ktokolwiek przeszukał gruzy. Moi sąsiedzi wciąż tam byli.", "Mandaram as #escavadeiras antes de alguém vasculhar os escombros. Meus vizinhos ainda estavam lá dentro.", "Прислали #бульдозеры, прежде чем кто-то обыскал завалы. Мои соседи всё ещё были там.", "还没人搜救废墟，就先派来了#推土机。我的邻居们还在里面。" },
                new[] { "No rescue crew, no search, just a demolition order. @mayor, people were trapped in that building.", "Kein Rettungsteam, keine Suche, nur ein Abrissbefehl. @Bürgermeister, in dem Gebäude waren Menschen eingeschlossen.", "Ni equipo de rescate, ni búsqueda, solo una orden de demolición. @alcalde, había gente atrapada en ese edificio.", "Pas d'équipe de secours, pas de recherches, juste un ordre de démolition. @maire, il y avait des gens coincés dans ce bâtiment.", "구조대도, 수색도 없이 철거 명령만. @시장님, 그 건물에 사람들이 갇혀 있었다고요.", "Żadnej ekipy ratunkowej, żadnych poszukiwań, tylko nakaz rozbiórki. @burmistrz, w tym budynku byli uwięzieni ludzie.", "Nenhuma equipe de resgate, nenhuma busca, só uma ordem de demolição. @prefeito, tinha gente presa naquele prédio.", "Ни спасателей, ни поисков, только приказ о сносе. @mayor, в этом здании были люди под завалами.", "没有救援队，没有搜救，只有一纸拆除令。@市长 那栋楼里还有人被困着。" }
            },
            ["Festival"] = new[]
            {
                new[] { "#Festival day: {0} is celebrating, streets full and hearts fuller.", "#Festtag: {0} feiert, die Straßen sind voll und die Herzen noch voller.", "#DíaDeFiesta: {0} está de celebración, calles llenas y corazones aún más llenos.", "#JourDeFête : {0} fait la fête, rues pleines et cœurs encore plus pleins.", "#축제날 {0} 잔치 중! 거리도 꽉, 마음도 꽉.", "#DzieńŚwięta: {0} świętuje, ulice pełne, serca jeszcze pełniejsze.", "#DiaDeFesta: {0} está em festa, ruas cheias e corações mais cheios ainda.", "#Праздник: {0} празднует, улицы полны, а сердца ещё полнее.", "#节日 {0}正在庆祝，街道满满，心里更满。" },
                new[] { "Half our street is closed while {0} celebrates, and the #festival food is worth every detour.", "Die halbe Straße ist gesperrt, weil {0} feiert, und das Essen beim #Fest ist jeden Umweg wert.", "Media calle cortada porque {0} está de celebración, y la comida del #festival vale cada desvío.", "La moitié de la rue est fermée parce que {0} fait la fête, et la cuisine de la #fête vaut tous les détours.", "{0} 잔치 때문에 우리 길 반이 막혔는데, #축제 음식이 너무 맛있어서 용서함.", "Pół ulicy zamknięte, bo {0} świętuje, ale jedzenie na #festynie jest warte każdego objazdu.", "Metade da rua fechada porque {0} está em festa, e a comida do #festival vale cada desvio.", "Пол-улицы перекрыто, потому что {0} празднует, но еда на #празднике стоит любого объезда.", "{0}在庆祝，半条街都封了，不过#节日美食值得绕路。" },
            },
            ["WorshipFar"] = new[]
            {
                new[] { "Our nearest place of worship is across town, so our #neighborhood just meets in someone's living room.", "Unser nächstes Gotteshaus ist am anderen Ende der Stadt, also trifft sich unsere #Nachbarschaft eben im Wohnzimmer von jemandem.", "Nuestro lugar de culto más cercano está al otro lado de la ciudad, así que el #barrio se reúne en el salón de alguien.", "Notre lieu de culte le plus proche est à l'autre bout de la ville, alors le #quartier se retrouve dans le salon de quelqu'un.", "제일 가까운 예배 장소가 도시 반대편이라 우리 #동네 사람들은 그냥 누구네 거실에서 모임.", "Najbliższe miejsce kultu mamy na drugim końcu miasta, więc nasza #okolica spotyka się po prostu w czyimś salonie.", "Nosso local de culto mais próximo fica do outro lado da cidade, então o #bairro se reúne na sala de alguém.", "Ближайшее место поклонения — на другом конце города, так что наш #район собирается у кого-нибудь в гостиной.", "离我们最近的礼拜场所在城市另一头，所以我们#街坊干脆在谁家客厅聚会。" },
                new[] { "Our congregation keeps growing but there's nowhere left to sit on worship day.", "Unsere Gemeinde wächst und wächst, aber am Gottesdiensttag gibt es keinen freien Platz mehr.", "Nuestra congregación no para de crecer, pero el día de culto ya no queda sitio para sentarse.", "Notre congrégation ne cesse de grandir, mais le jour du culte il n'y a plus une place assise.", "우리 공동체는 계속 커지는데 예배일엔 앉을 자리가 하나도 없음.", "Nasza wspólnota ciągle rośnie, ale w dzień modlitwy nie ma już gdzie usiąść.", "Nossa congregação não para de crescer, mas no dia de culto não sobra lugar para sentar.", "Наша община всё растёт, а в день служения уже негде сесть.", "我们的信众越来越多，可礼拜日已经找不到座位了。" },
            },
            ["NewWorship"] = new[]
            {
                new[] { "A new place of worship just opened nearby. Looking forward to the first gathering!", "In der Nähe hat gerade ein neues Gotteshaus eröffnet. Ich freue mich schon auf die erste Zusammenkunft!", "Acaba de abrir un nuevo lugar de culto cerca. ¡Con ganas de la primera reunión!", "Un nouveau lieu de culte vient d'ouvrir près d'ici. Vivement le premier rassemblement !", "근처에 새 예배 장소 오픈! 첫 모임 완전 기대됨.", "Niedaleko właśnie otwarto nowe miejsce kultu. Już nie mogę się doczekać pierwszego spotkania!", "Um novo local de culto acabou de abrir aqui perto. Ansioso pelo primeiro encontro!", "Рядом только что открылось новое место поклонения. Жду не дождусь первой встречи!", "附近刚开了一座新的礼拜场所。期待第一次聚会！" },
                new[] { "#Grandopening of the new place of worship down the road. They had cake. I'm going back.", "#Eröffnung des neuen Gotteshauses die Straße runter. Es gab Kuchen. Ich gehe wieder hin.", "#Inauguración del nuevo lugar de culto calle abajo. Había tarta. Pienso volver.", "#Inauguration du nouveau lieu de culte au bout de la rue. Il y avait du gâteau. J'y retournerai.", "#개관 길 아래 새 예배 장소 열었는데 케이크 줌. 또 갈 거임.", "#Otwarcie nowego miejsca kultu przy naszej ulicy. Był tort. Na pewno wrócę.", "#Inauguração do novo local de culto aqui na rua. Teve bolo. Vou voltar.", "#Открытие нового места поклонения в конце улицы. Был торт. Пойду ещё.", "#开幕 街那头的新礼拜场所开张了，还有蛋糕。我还会去的。" },
            },
            ["Budget"] = new[]
            {
                new[] { "Rent ate the whole paycheck again, so date night is #homecooking and a walk around the block.", "Die Miete hat wieder das ganze Gehalt gefressen, also heißt Date-Abend: #selbstgekocht und ein Spaziergang um den Block.", "El alquiler se volvió a comer todo el sueldo, así que la cita de esta noche es #comidacasera y un paseo por el barrio.", "Le loyer a encore englouti tout le salaire, alors le rendez-vous de ce soir, c'est #faitmaison et un tour du pâté de maisons.", "월세가 또 월급 다 먹어 버림. 그래서 오늘 데이트는 #집밥 이랑 동네 한 바퀴.", "Czynsz znowu zjadł całą pensję, więc randka to #domoweobiadki i spacer wokół bloku.", "O aluguel comeu o salário inteiro de novo, então o encontro de hoje é #comidacaseira e uma volta no quarteirão.", "Аренда снова съела всю зарплату, так что свидание сегодня — #домашняяеда и прогулка вокруг квартала.", "房租又吃掉了整份工资，所以今晚约会就是#家常菜加绕小区散步。" },
                new[] { "#Shoplocal, they say. With what money? After rent, there's nothing left for the stores on our street.", "#KauftLokal, heißt es. Mit welchem Geld? Nach der Miete bleibt für die Läden in unserer Straße nichts übrig.", "#Compralocal, dicen. ¿Con qué dinero? Después del alquiler no queda nada para las tiendas de nuestra calle.", "#Achetezlocal, qu'ils disent. Avec quel argent ? Après le loyer, il ne reste rien pour les commerces de notre rue.", "#동네가게살리기 하라고? 무슨 돈으로? 월세 내고 나면 우리 동네 가게에 쓸 돈이 하나도 없음.", "#Kupujlokalnie, mówią. Za co? Po opłaceniu czynszu na sklepy przy naszej ulicy nic nie zostaje.", "#Compredopequeno, dizem. Com que dinheiro? Depois do aluguel, não sobra nada para as lojas da nossa rua.", "#Покупайулокальных, говорят. На какие деньги? После аренды на магазины нашей улицы ничего не остаётся.", "#支持本地小店？拿什么钱？交完房租，街上的店我们一分钱也花不起了。" },
            },
            ["Frozen"] = new[]
            {
                new[] { "Someone froze to death in a doorway down the street last night. How is that still happening here? #sheltersnow", "Letzte Nacht ist jemand in einem Hauseingang unten an der Straße erfroren. Wie kann das hier immer noch passieren? #NotunterkünfteJetzt", "Anoche alguien murió de frío en un portal de nuestra calle. ¿Cómo sigue pasando esto aquí? #AlberguesYa", "Quelqu'un est mort de froid cette nuit sous un porche de notre rue. Comment ça peut encore arriver ici ? #DesAbrisMaintenant", "어젯밤 우리 동네 건물 입구에서 누가 얼어 죽었대. 아직도 이런 일이 생긴다고? #쉼터가필요해", "Ktoś zamarzł zeszłej nocy w bramie przy naszej ulicy. Jak to się wciąż może tu dziać? #SchroniskaTeraz", "Alguém morreu de frio ontem à noite numa porta aqui da rua. Como isso ainda acontece aqui? #AbrigosJá", "Прошлой ночью в подъезде на нашей улице кто-то замёрз насмерть. Как такое у нас всё ещё возможно? #ПриютыСейчас", "昨晚街那头的门洞里有人冻死了。这种事怎么还会发生？#现在就要收容所" },
                new[] { "Volunteers handed out blankets all night, but the #coldsnap still took someone sleeping rough by the park.", "Ehrenamtliche haben die ganze Nacht Decken verteilt, aber die #Kältewelle hat trotzdem jemanden geholt, der am Park auf der Straße schlief.", "Los voluntarios repartieron mantas toda la noche, pero la #olaDeFrío se llevó igualmente a alguien que dormía al raso junto al parque.", "Des bénévoles ont distribué des couvertures toute la nuit, mais la #vagueDeFroid a quand même emporté quelqu'un qui dormait dehors près du parc.", "봉사자들이 밤새 담요를 나눠 줬는데도 #한파 에 공원 옆에서 노숙하던 사람이 결국 세상을 떠났어.", "Wolontariusze całą noc rozdawali koce, a #mróz i tak zabrał kogoś, kto spał na dworze przy parku.", "Voluntários distribuíram cobertores a noite toda, mas a #ondaDeFrio ainda levou alguém que dormia na rua perto do parque.", "Волонтёры всю ночь раздавали одеяла, но #холода всё равно унесли человека, ночевавшего на улице у парка.", "志愿者整夜都在发毯子，可这场#寒潮还是带走了一个睡在公园边的人。" },
            },
            ["Heatstroke"] = new[]
            {
                new[] { "#Heatwave day three. A man who'd been sleeping on the sidewalk collapsed outside the store and didn't make it.", "#Hitzewelle, Tag drei. Ein Mann, der auf dem Gehweg schlief, ist vor dem Laden zusammengebrochen und hat es nicht geschafft.", "#OlaDeCalor, día tres. Un hombre que dormía en la acera se desplomó frente a la tienda y no sobrevivió.", "#Canicule, jour trois. Un homme qui dormait sur le trottoir s'est effondré devant la boutique et n'a pas survécu.", "#폭염 사흘째. 인도에서 자던 아저씨가 가게 앞에서 쓰러졌는데 결국 못 일어나셨대.", "#Upał, dzień trzeci. Mężczyzna, który spał na chodniku, zasłabł przed sklepem i nie przeżył.", "#OndaDeCalor, terceiro dia. Um homem que dormia na calçada desmaiou em frente à loja e não resistiu.", "#Жара, третий день. Мужчина, ночевавший на тротуаре, упал у магазина, и спасти его не удалось.", "#热浪 第三天。一个睡在人行道上的男人在店门口倒下，没能救回来。" },
                new[] { "Cooling centers would cost less than the ambulances we keep calling for people sleeping outside in this heat. #citycouncil", "Kühlräume würden weniger kosten als die Krankenwagen, die wir bei dieser Hitze ständig für Menschen auf der Straße rufen. #Stadtrat", "Los refugios climáticos costarían menos que las ambulancias que llamamos una y otra vez para la gente que duerme fuera con este calor. #ayuntamiento", "Des salles rafraîchies coûteraient moins cher que les ambulances qu'on appelle sans arrêt pour les gens qui dorment dehors par cette chaleur. #conseilmunicipal", "이 더위에 밖에서 자는 사람들 때문에 계속 구급차 부르는 것보다 무더위 쉼터 만드는 게 더 쌀 듯. #시의회", "Klimatyzowane punkty kosztowałyby mniej niż karetki, które w ten upał ciągle wzywamy do ludzi śpiących na dworze. #radamiasta", "Centros de resfriamento custariam menos que as ambulâncias que vivemos chamando para quem dorme na rua neste calor. #câmaramunicipal", "Пункты охлаждения обошлись бы дешевле, чем скорые, которые мы в эту жару постоянно вызываем к людям на улице. #горсовет", "这么热的天，一次次为睡在外面的人叫救护车，还不如开几个纳凉点便宜。#市议会" },
            },
            ["ValueDrop"] = new[]
            {
                new[] { "Had the house valued today. Worth a lot less than when we bought it, all thanks to what they built at the end of the road. #propertyvalues", "Haus heute schätzen lassen. Viel weniger wert als beim Kauf, alles dank dem, was sie am Ende der Straße gebaut haben. #Immobilienwerte", "Hoy tasaron la casa. Vale mucho menos que cuando la compramos, gracias a lo que construyeron al final de la calle. #valordelavivienda", "Fait estimer la maison aujourd'hui. Elle vaut bien moins qu'à l'achat, merci à ce qu'ils ont construit au bout de la rue. #valeurimmobilière", "오늘 집값 감정 받았는데 살 때보다 확 떨어짐. 다 길 끝에 지은 그거 때문임. #집값", "Dziś wyceniali nasz dom. Wart dużo mniej niż przy zakupie, wszystko przez to, co postawili na końcu ulicy. #wartośćnieruchomości", "Avaliaram a casa hoje. Vale bem menos do que quando compramos, graças ao que construíram no fim da rua. #valordoimóvel", "Сегодня оценили дом. Стоит намного меньше, чем при покупке, спасибо тому, что построили в конце улицы. #ценынажильё", "今天给房子估了价，比买的时候跌了一大截，全拜路尽头新建的那个东西所赐。#房价" },
                new[] { "#Homeowners meeting tonight. We've watched our property values slide for months and nobody at city hall seems to care.", "#Eigentümerversammlung heute Abend. Seit Monaten sehen wir unsere Immobilienwerte sinken und im Rathaus scheint das niemanden zu kümmern.", "#ReuniónDePropietarios esta noche. Llevamos meses viendo caer el valor de nuestras casas y en el ayuntamiento a nadie parece importarle.", "#RéunionDesPropriétaires ce soir. Ça fait des mois que la valeur de nos maisons baisse et ça ne semble intéresser personne à la mairie.", "#집주인모임 오늘 저녁. 몇 달째 집값이 쭉 떨어지는데 시청은 신경도 안 쓰는 듯.", "#ZebranieWłaścicieli dziś wieczorem. Od miesięcy patrzymy, jak wartość naszych domów spada, a w ratuszu nikogo to nie obchodzi.", "#ReuniãoDeProprietários hoje à noite. Há meses vemos nossos imóveis perderem valor e ninguém na prefeitura parece se importar.", "#СобраниеСобственников сегодня вечером. Месяцами смотрим, как дешевеют наши дома, а в мэрии, похоже, всем всё равно.", "#业主大会 今晚开。房价连跌了好几个月，市政厅好像根本不在乎。" },
            },
            ["ShortStay"] = new[]
            {
                new[] { "Half the apartments in our building are #vacationrentals now. New faces with suitcases every week, and the rent keeps climbing.", "Die Hälfte der Wohnungen in unserem Haus sind jetzt #Ferienwohnungen. Jede Woche neue Gesichter mit Koffern, und die Miete steigt weiter.", "La mitad de los pisos de nuestro edificio ya son #pisosturísticos. Caras nuevas con maletas cada semana, y el alquiler no para de subir.", "La moitié des appartements de l'immeuble sont devenus des #locationsdecourtedurée. De nouvelles têtes avec des valises chaque semaine, et le loyer qui grimpe.", "우리 건물 집 절반이 이제 #단기임대 야. 매주 캐리어 끄는 새 얼굴들, 월세는 계속 오르고.", "Połowa mieszkań w naszym bloku to teraz #wynajemkrótkoterminowy. Co tydzień nowe twarze z walizkami, a czynsz dalej rośnie.", "Metade dos apartamentos do nosso prédio agora é #aluguelporTemporada. Caras novas com malas toda semana, e o aluguel não para de subir.", "Половина квартир в нашем доме теперь #посуточнаяаренда. Каждую неделю новые лица с чемоданами, а аренда всё растёт.", "我们楼里一半的房子都成了#短租。每周都有拖着行李箱的新面孔，房租还一直在涨。" },
                new[] { "Tried to find an apartment near the old town. Every listing was a nightly rental for tourists.", "Habe versucht, eine Wohnung nahe der Altstadt zu finden. Jedes Angebot war eine Übernachtungsunterkunft für Touristen.", "Intenté encontrar piso cerca del casco antiguo. Todos los anuncios eran alquileres por noches para turistas.", "J'ai cherché un appart près de la vieille ville. Toutes les annonces étaient des locations à la nuitée pour touristes.", "구시가지 근처에서 집 구해 보려 했는데 올라온 매물이 죄다 관광객용 1박 숙소였음.", "Szukałem mieszkania blisko starówki. Każde ogłoszenie to był nocleg dla turystów.", "Tentei achar um apartamento perto do centro histórico. Todo anúncio era aluguel por diária para turistas.", "Пытался найти квартиру рядом со старым городом. Все объявления — посуточная аренда для туристов.", "想在老城附近找个房子，结果所有房源都是给游客的按晚出租。" },
            },
        };

        const uint Gap = 16384, Global = 2048;
        static readonly System.Random random = new System.Random();
        static readonly Dictionary<string, uint> last = new Dictionary<string, uint>();
        static uint lastAny;
        static bool any;

        internal static void Reset()
        {
            lock (last) { last.Clear(); any = false; }
        }

        internal static void Send(string id, uint sender, params object[] args)
        {
            if (!Text.TryGetValue(id, out string[][] variants) || variants.Length == 0) return;
            int pick;
            lock (random) pick = random.Next(variants.Length);
            string template = Lang.Pick(variants[pick]);
            if (string.IsNullOrEmpty(template)) return;
            uint now = Sim.Frame;
            lock (last)
            {
                if (any && now - lastAny < Global || last.TryGetValue(id, out uint at) && now - at < Gap) return;
                last[id] = lastAny = now;
                any = true;
            }
            if (sender == 0) sender = Singleton<MessageManager>.instance.GetRandomResidentID();
            string name = sender != 0 ? Singleton<CitizenManager>.instance.GetCitizenName(sender) : null;
            var message = new Message { senderName = string.IsNullOrEmpty(name) ? "Chirper" : name, senderID = sender, text = args.Length > 0 ? Lang.F(template, args) : template };
            ThreadHelper.dispatcher.Dispatch(() => { if (ChirpPanel.instance != null) ChirpPanel.instance.AddMessage(message); });
        }

        class Message : IChirperMessage
        {
            public string senderName { get; set; }
            public string text { get; set; }
            public uint senderID { get; set; }
        }
    }

    public class Loading : LoadingExtensionBase
    {
        static GameObject ticker;

        public override void OnLevelLoaded(LoadMode mode)
        {
            Settings.Init();
            if ((Singleton<ToolManager>.instance.m_properties.m_mode & ItemClass.Availability.Game) == 0) return;
            Sim.Start();
            try { StatsWindow.Create(); } catch (Exception e) { Debug.LogException(e); }
            if (ticker == null) ticker = new GameObject("CityConsequencesMaps").AddComponent<MapTicker>().gameObject;
        }

        public override void OnLevelUnloading()
        {
            StatsWindow.Remove();
            Badges.Clear();
            Maps.Clear();
            if (ticker != null) UnityEngine.Object.Destroy(ticker);
            ticker = null;
            Sim.Stop();
        }
    }

    public class Data : SerializableDataExtensionBase
    {
        const string WearKey = "CityConsequences.wear";
        const string Key = "CityConsequences.v5", RecordKey = "CityConsequences.records", FaithKey = "CityConsequences.religion", LegacyFaithKey = "PlacesOfWorship";

        public override void OnLoadData()
        {
            try { Housing.Load(serializableDataManager.LoadData(Key)); }
            catch (Exception e) { Debug.LogException(e); Housing.Load(null); }
            try { Records.Load(serializableDataManager.LoadData(RecordKey)); }
            catch (Exception e) { Debug.LogException(e); Records.Load(null); }
            try { Wear.Load(serializableDataManager.LoadData(WearKey)); }
            catch (Exception e) { Debug.LogException(e); Wear.Load(null); }
            if (Religion.Standalone) return;
            try
            {
                byte[] faith = serializableDataManager.LoadData(FaithKey);
                bool legacy = faith == null;
                if (legacy) faith = serializableDataManager.LoadData(LegacyFaithKey);
                Religion.Load(faith, legacy && faith != null);
            }
            catch (Exception e) { Debug.LogException(e); Religion.Load(null, false); }
        }

        public override void OnSaveData()
        {
            try { serializableDataManager.SaveData(Key, Housing.Save()); }
            catch (Exception e) { Debug.LogException(e); }
            try { serializableDataManager.SaveData(RecordKey, Records.Save()); }
            catch (Exception e) { Debug.LogException(e); }
            try { serializableDataManager.SaveData(WearKey, Wear.Save()); }
            catch (Exception e) { Debug.LogException(e); }
            if (Religion.Standalone) return;
            try
            {
                byte[] faith = Religion.Save();
                if (faith != null) serializableDataManager.SaveData(FaithKey, faith);
            }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    static class Records
    {
        const uint Jail = uint.MaxValue;
        static readonly Dictionary<uint, uint> until = new Dictionary<uint, uint>();
        static readonly object sync = new object();

        internal static int InJail { get; private set; }
        internal static int Barred { get; private set; }

        static uint Length => (uint)Mathf.Max(0f, Settings.RecordWeeks.value) * 4096u;

        internal static bool Blocked(uint c)
        {
            if (until.Count == 0) return false;
            lock (sync) return until.TryGetValue(c, out uint u) && (u == Jail || (int)(u - Sim.Frame) > 0);
        }

        internal static void Arrest(uint c, ref Citizen data)
        {
            if (Length == 0) return;
            lock (sync)
            {
                if (until.TryGetValue(c, out uint u) && u == Jail) return;
                until[c] = Jail;
            }
            if (data.m_workBuilding != 0 && (data.m_flags & Citizen.Flags.Student) == 0) data.SetWorkplace(c, 0, 0u);
        }

        internal static void Tick(uint c, ref Citizen data)
        {
            if (data.Arrested) { Arrest(c, ref data); return; }
            if (until.Count == 0) return;
            lock (sync)
            {
                if (!until.TryGetValue(c, out uint u)) return;
                if (u == Jail) until[c] = Sim.Frame + Math.Max(1u, Length);
                else if ((int)(u - Sim.Frame) <= 0) until.Remove(c);
            }
        }

        internal static void Forget(uint c)
        {
            if (until.Count == 0) return;
            lock (sync) until.Remove(c);
        }

        internal static void Count()
        {
            int jail = 0, barred = 0;
            lock (sync)
                foreach (var kv in until)
                {
                    if (kv.Value == Jail) jail++;
                    else if ((int)(kv.Value - Sim.Frame) > 0) barred++;
                }
            InJail = jail;
            Barred = barred;
        }

        internal static void Clear()
        {
            lock (sync) until.Clear();
            InJail = Barred = 0;
        }

        internal static byte[] Save()
        {
            using (var m = new System.IO.MemoryStream())
            using (var w = new System.IO.BinaryWriter(m))
            {
                Citizen[] z = Singleton<CitizenManager>.instance.m_citizens.m_buffer;
                var keep = new List<KeyValuePair<uint, uint>>();
                lock (sync)
                    foreach (var kv in until)
                        if (kv.Key < z.Length && (z[kv.Key].m_flags & Citizen.Flags.Created) != 0) keep.Add(kv);
                w.Write(1);
                w.Write(Singleton<SimulationManager>.instance.m_currentFrameIndex);
                w.Write(keep.Count);
                foreach (var kv in keep)
                {
                    w.Write(kv.Key);
                    w.Write(kv.Value);
                }
                w.Flush();
                return m.ToArray();
            }
        }

        internal static void Load(byte[] data)
        {
            Clear();
            if (data == null || data.Length < 12) return;
            using (var r = new System.IO.BinaryReader(new System.IO.MemoryStream(data)))
            {
                if (r.ReadInt32() != 1) return;
                r.ReadUInt32();
                int n = r.ReadInt32();
                lock (sync)
                    for (int i = 0; i < n; i++)
                    {
                        uint c = r.ReadUInt32(), u = r.ReadUInt32();
                        until[c] = u;
                    }
            }
        }
    }

    public class Economy : EconomyExtensionBase
    {
        public override int OnAddResource(EconomyResource resource, int amount, Service service, SubService subService, Level level) =>
            resource == EconomyResource.PrivateIncome && Housing.Current != 0 ? Housing.TaxIncome(Housing.Current, amount) : amount;
    }

    public class Sim : ThreadingExtensionBase
    {
        internal const uint YearFrames = 16384;
        const int ErrorLimit = 32;
        internal static volatile bool Active;
        internal static uint Frame;
        static readonly string[] parts = { "Walkability", "Roads", "Housing", "Religion" };
        static readonly int[] errors = new int[4];
        static readonly bool[] off = new bool[4];

        internal static void Start()
        {
            Array.Clear(errors, 0, errors.Length);
            Array.Clear(off, 0, off.Length);
            Frame = Singleton<SimulationManager>.instance.m_currentFrameIndex;
            Safe("Roads.Start", Roads.Start);
            Safe("Payroll.Reset", Payroll.Reset);
            Safe("Difficulty", Difficulty.Detect);
            Safe("Wear.Start", Wear.Start);
            Safe("Walk.Reset", Walk.Reset);
            Safe("Politics.Start", Politics.Start);
            Safe("Tents.Reset", Tents.Reset);
            Safe("Chirps.Reset", Chirps.Reset);
            Safe("Housing.Start", () => Housing.Start(Frame));
            Safe("Religion.Start", Religion.Start);
            Active = true;
        }

        static void Safe(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { Debug.LogError("[City Consequences] " + what + " failed"); Debug.LogException(e); }
        }

        static void Fail(int part, Exception e)
        {
            int n = ++errors[part];
            if (n <= 3) { Debug.LogError("[City Consequences] " + parts[part] + " error " + n); Debug.LogException(e); }
            if (n < ErrorLimit) return;
            off[part] = true;
            Debug.LogError("[City Consequences] " + parts[part] + " disabled for this session after repeated errors. Everything else keeps running.");
        }

        internal static void Stop()
        {
            Active = false;
            Housing.Clear();
            Records.Clear();
            Religion.Stop();
        }

        public override void OnAfterSimulationFrame()
        {
            if (!Active) return;
            Frame = Singleton<SimulationManager>.instance.m_currentFrameIndex;
            if (!off[0]) try { Walk.Step(Frame); } catch (Exception e) { Fail(0, e); }
            if (!off[1]) try { Roads.Step(Frame); } catch (Exception e) { Fail(1, e); }
            if (!off[2]) try { Housing.Step(Frame); } catch (Exception e) { Fail(2, e); }
            if (!off[3]) try { if (Religion.Enabled && Religion.C[0] != null) Religion.Step(); } catch (Exception e) { Fail(3, e); }
        }

        internal static int Roll(uint max) => Singleton<SimulationManager>.instance.m_randomizer.Int32(max);
    }

    static class Walk
    {
        const int N = 270, R = 7, Cells = N * N, BuildingsPerFrame = 49152 / 256, NodesPerFrame = 32768 / 256, Lines = 256;
        const float Dwell = 0.25f;

        static float[] shops = new float[Cells], jobs = new float[Cells], services = new float[Cells], transit = new float[Cells];
        static float[] nShops = new float[Cells], nJobs = new float[Cells], nServices = new float[Cells], nTransit = new float[Cells];
        static readonly float[] lineValue = new float[Lines];
        static readonly int[] kx, kz;
        static readonly float[] kw;
        static float meanSum;
        static int meanCount;
        internal static float Mean = 40f;
        internal static bool Ready;

        static Walk() => Kernel(R, out kx, out kz, out kw);

        internal static void Kernel(int r, out int[] x, out int[] z, out float[] w)
        {
            var lx = new List<int>(); var lz = new List<int>(); var lw = new List<float>();
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d > r + 0.5f) continue;
                    lx.Add(dx); lz.Add(dz); lw.Add(1f - d / (r + 1f));
                }
            x = lx.ToArray(); z = lz.ToArray(); w = lw.ToArray();
        }

        internal static void Reset()
        {
            Ready = false;
            Mean = 40f;
            meanSum = 0f;
            meanCount = 0;
            Array.Clear(lineValue, 0, Lines);
            foreach (float[] g in new[] { shops, jobs, services, transit, nShops, nJobs, nServices, nTransit }) Array.Clear(g, 0, g.Length);
        }

        internal static int Cell(Vector3 p)
        {
            int x = (int)(p.x / 64f + 135f), z = (int)(p.z / 64f + 135f);
            return x < 0 || z < 0 || x >= N || z >= N ? -1 : z * N + x;
        }

        internal static float Sum(float[] grid, Vector3 p, int[] ox, int[] oz, float[] ow)
        {
            int cx = (int)(p.x / 64f + 135f), cz = (int)(p.z / 64f + 135f);
            float s = 0f;
            for (int k = 0; k < ow.Length; k++)
            {
                int x = cx + ox[k], z = cz + oz[k];
                if (x >= 0 && z >= 0 && x < N && z < N) s += grid[z * N + x] * ow[k];
            }
            return s;
        }

        static float Speed(TransportInfo.TransportType t)
        {
            switch (t)
            {
                case TransportInfo.TransportType.Bus: case TransportInfo.TransportType.CableCar: return 1f;
                case TransportInfo.TransportType.Trolleybus: return 1.1f;
                case TransportInfo.TransportType.Ship: return 1.2f;
                case TransportInfo.TransportType.Tram: return 1.3f;
                case TransportInfo.TransportType.Airplane: case TransportInfo.TransportType.Helicopter: return 1.5f;
                case TransportInfo.TransportType.Monorail: return 2f;
                case TransportInfo.TransportType.Metro: return 2.5f;
                case TransportInfo.TransportType.Train: return 3f;
            }
            return 0f;
        }

        static void LineService()
        {
            TransportLine[] l = Singleton<TransportManager>.instance.m_lines.m_buffer;
            for (int i = 1; i < Lines; i++)
            {
                lineValue[i] = 0f;
                if ((l[i].m_flags & (TransportLine.Flags.Created | TransportLine.Flags.Complete)) != (TransportLine.Flags.Created | TransportLine.Flags.Complete)) continue;
                TransportInfo info = l[i].Info;
                float speed = info != null ? Speed(info.m_transportType) : 0f;
                if (speed <= 0f) continue;
                int stops = l[i].CountStops((ushort)i), vehicles = l[i].CountVehicles((ushort)i);
                if (stops < 2 || vehicles == 0) continue;
                float km = Mathf.Max(0.5f, l[i].m_totalLength / 1000f);
                float effective = 1f / (1f / speed + Dwell * stops / km);
                lineValue[i] = Sat(vehicles * effective / km, 0.5f) * Mathf.Lerp(0.7f, 1.3f, Mathf.Clamp01(effective / 2.5f));
            }
        }

        internal static void Step(uint frame)
        {
            int slot = (int)(frame & 255);
            if (slot == 0) LineService();
            Building[] b = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            for (int i = slot * BuildingsPerFrame, end = i + BuildingsPerFrame; i < end; i++)
            {
                if ((b[i].m_flags & (Building.Flags.Created | Building.Flags.Completed | Building.Flags.Deleted | Building.Flags.Abandoned | Building.Flags.Collapsed)) != (Building.Flags.Created | Building.Flags.Completed)) continue;
                BuildingInfo info = b[i].Info;
                int c = Cell(b[i].m_position);
                if (info == null || c < 0) continue;
                float area = b[i].Width * b[i].Length;
                switch (info.m_class.m_service)
                {
                    case ItemClass.Service.Residential:
                        if (Ready && (i & 7) == 0) { meanSum += Access(b[i].m_position); meanCount++; }
                        break;
                    case ItemClass.Service.Commercial: nShops[c] += area; nJobs[c] += area * 0.5f; break;
                    case ItemClass.Service.Office: nJobs[c] += area * (1 + b[i].m_level); break;
                    case ItemClass.Service.Education:
                    case ItemClass.Service.HealthCare:
                    case ItemClass.Service.Beautification:
                    case ItemClass.Service.PoliceDepartment:
                    case ItemClass.Service.Monument: nServices[c] += 1f; break;
                }
            }
            NetNode[] n = Singleton<NetManager>.instance.m_nodes.m_buffer;
            for (int i = slot * NodesPerFrame, end = i + NodesPerFrame; i < end; i++)
            {
                ushort line = n[i].m_transportLine;
                if (line == 0 || (n[i].m_flags & NetNode.Flags.Created) == 0) continue;
                int c = Cell(n[i].m_position);
                if (c >= 0) nTransit[c] += lineValue[line];
            }
            if (slot != 255) return;
            Swap(ref shops, ref nShops); Swap(ref jobs, ref nJobs); Swap(ref services, ref nServices); Swap(ref transit, ref nTransit);
            foreach (float[] g in new[] { nShops, nJobs, nServices, nTransit }) Array.Clear(g, 0, g.Length);
            if (meanCount >= 16) Mean = Mathf.Max(5f, meanSum / meanCount);
            meanSum = 0f;
            meanCount = 0;
            Ready = true;
        }

        static void Swap(ref float[] a, ref float[] b) { float[] t = a; a = b; b = t; }

        internal static float Access(Vector3 p)
        {
            if (!Ready) return 0f;
            float walk = 100f * (0.45f * Sat(Sum(shops, p, kx, kz, kw), 12f) + 0.35f * Sat(Sum(jobs, p, kx, kz, kw), 20f) + 0.2f * Sat(Sum(services, p, kx, kz, kw), 4f));
            return 0.65f * walk + 0.35f * 100f * Sat(Sum(transit, p, kx, kz, kw), 1.5f);
        }

        internal static float Local(Vector3 p) => Ready ? 0.55f * Sat(Sum(shops, p, kx, kz, kw), 12f) + 0.45f * Sat(Sum(jobs, p, kx, kz, kw), 20f) : 0.5f;

        internal static float Sat(float x, float scale) => 1f - Mathf.Exp(-x / scale);
        internal static float TransitAt(Vector3 p) => Ready ? Sat(Sum(transit, p, kx, kz, kw), 1.5f) : 1f;
        internal static float ServicesAt(Vector3 p) => Ready ? Sat(Sum(services, p, kx, kz, kw), 4f) : 1f;
    }

    public class LevelUp : LevelUpExtensionBase
    {
        const float LowDensity = 0.6f;
        static readonly float[] ResRel = { 0.45f, 0.6f, 0.75f, 0.9f }, ResFloor = { 5f, 10f, 15f, 20f }, ResCap = { 19f, 38f, 56f, 75f };
        static readonly float[] ComRel = { 0.6f, 0.9f }, ComFloor = { 8f, 15f }, ComCap = { 31f, 56f };
        static readonly float[] OffRel = { 0.7f, 0.95f }, OffFloor = { 10f, 17f }, OffCap = { 38f, 63f };

        static Level Cap(Level target, Level current, ushort id, float[] rel, float[] floor, float[] cap, bool low, bool residential)
        {
            if (!Sim.Active || target <= current) return target;
            if (residential && Housing.Controlled(id) && Sim.Roll(100) < Housing.SlowGrowth) return current;
            if (!Settings.Walk.value || !Walk.Ready) return target;
            float a = Walk.Access(Singleton<BuildingManager>.instance.m_buildings.m_buffer[id].m_position);
            int allowed = 0;
            while (allowed < rel.Length && a >= Mathf.Clamp(rel[allowed] * Walk.Mean, floor[allowed], cap[allowed]) * (low ? LowDensity : 1f)) allowed++;
            return (Level)Math.Max((int)current, Math.Min((int)target, allowed));
        }

        public override ResidentialLevelUp OnCalculateResidentialLevelUp(ResidentialLevelUp levelUp, int averageEducation, int landValue, ushort buildingID, Service service, SubService subService, Level currentLevel)
        {
            var s = (ItemClass.SubService)subService;
            levelUp.targetLevel = Cap(levelUp.targetLevel, currentLevel, buildingID, ResRel, ResFloor, ResCap, s == ItemClass.SubService.ResidentialLow || s == ItemClass.SubService.ResidentialLowEco, true);
            return levelUp;
        }

        public override CommercialLevelUp OnCalculateCommercialLevelUp(CommercialLevelUp levelUp, int averageWealth, int landValue, ushort buildingID, Service service, SubService subService, Level currentLevel)
        {
            levelUp.targetLevel = Cap(levelUp.targetLevel, currentLevel, buildingID, ComRel, ComFloor, ComCap, (ItemClass.SubService)subService == ItemClass.SubService.CommercialLow, false);
            return levelUp;
        }

        public override OfficeLevelUp OnCalculateOfficeLevelUp(OfficeLevelUp levelUp, int averageEducation, int serviceScore, ushort buildingID, Service service, SubService subService, Level currentLevel)
        {
            levelUp.targetLevel = Cap(levelUp.targetLevel, currentLevel, buildingID, OffRel, OffFloor, OffCap, false, false);
            return levelUp;
        }
    }

    static class Roads
    {
        const float Base = 2.5f, WidthExponent = 0.5f, Bridge = 3f, Tunnel = 4f, KmPerThousand = 4f, NetworkExponent = 0.6f, MinPopulation = 2000f, MinRatio = 0.5f, MaxRatio = 4f;
        const int SegmentsPerFrame = 36864 / 256;

        static readonly Dictionary<NetInfo, float> prefab = new Dictionary<NetInfo, float>();
        static float metres;
        static bool ageing = true;
        static volatile float network = 1f;
        internal static float Network => network;

        internal static void Start()
        {
            prefab.Clear();
            metres = 0f;
            network = 1f;
            for (uint i = 0, count = (uint)PrefabCollection<NetInfo>.LoadedCount(); i < count; i++)
            {
                NetInfo info = PrefabCollection<NetInfo>.GetLoaded(i);
                if (info != null && info.m_netAI is RoadBaseAI) prefab[info] = Factor(info);
            }
        }

        static float Factor(NetInfo info)
        {
            float f = Base * Mathf.Pow(Mathf.Max(info.m_halfWidth, 4f) / 8f, WidthExponent);
            if (info.m_netAI is RoadBridgeAI) f *= Bridge;
            else if (info.m_netAI is RoadTunnelAI) f *= Tunnel;
            return f;
        }

        internal static void Step(uint frame)
        {
            int slot = (int)(frame & 255);
            NetSegment[] s = Singleton<NetManager>.instance.m_segments.m_buffer;
            for (int i = slot * SegmentsPerFrame, end = i + SegmentsPerFrame; i < end; i++)
            {
                if ((s[i].m_flags & (NetSegment.Flags.Created | NetSegment.Flags.Deleted)) != NetSegment.Flags.Created) continue;
                NetInfo info = s[i].Info;
                if (info == null) continue;
                if (info.m_netAI is RoadBaseAI) metres += s[i].m_averageLength;
                if (ageing) Wear.Tick(i, ref s[i], info);
            }
            if (slot != 255) return;
            Payroll.Update();
            Wear.EndPass();
            ageing = Settings.Ageing.value;
            float people = Mathf.Max(MinPopulation, Singleton<DistrictManager>.instance.m_districts.m_buffer[0].m_populationData.m_finalCount);
            network = Mathf.Pow(Mathf.Clamp(metres / people / KmPerThousand, MinRatio, MaxRatio), NetworkExponent);
            metres = 0f;
        }

        internal static int Apply(NetAI ai, int cost)
        {
            if (cost <= 0 || !Sim.Active || !Settings.Roads.value) return cost;
            if (!prefab.TryGetValue(ai.m_info, out float f)) f = Factor(ai.m_info);
            return Mathf.CeilToInt(cost * f * network * Settings.RoadScale.value);
        }
    }

    static class Difficulty
    {
        const int HardDemand = 20;
        static volatile bool hard;
        internal static bool Hard => hard;
        internal static int DemandOffset => hard ? HardDemand : 0;

        internal static void Detect()
        {
            hard = false;
            try
            {
                foreach (ColossalFramework.Plugins.PluginManager.PluginInfo p in ColossalFramework.Plugins.PluginManager.instance.GetPluginsInfo())
                {
                    if (p == null || !p.isEnabled) continue;
                    string type = p.userModInstance != null ? p.userModInstance.GetType().FullName : null;
                    string title = p.userModInstance is IUserMod mod ? mod.Name : null;
                    if (Is(p.name) || Is(type) || Is(title)) { hard = true; return; }
                }
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        static bool Is(string s) => s != null && s.Replace(" ", "").IndexOf("HardMode", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static class Payroll
    {
        const float RefLand = 35f, Staff = 0.6f, RampStart = 20000f, RampEnd = 80000f, MinWealth = 0.5f, MaxWealth = 3f, MinIndex = 0.9f, MaxIndex = 2.2f;
        const float WageFollow = 0.4f, WageMax = 1.4f, WageEase = 0.05f;
        static volatile float index = 1f, pay = 1f;
        internal static float Index => index;
        internal static float Pay => pay;

        internal static void Reset() => pay = 1f;

        internal static void Update()
        {
            DistrictManager dm = Singleton<DistrictManager>.instance;
            float people = dm.m_districts.m_buffer[0].m_populationData.m_finalCount;
            float ramp = Mathf.Clamp01((people - RampStart) / (RampEnd - RampStart));
            ramp = ramp * ramp * (3f - 2f * ramp);
            float wealth = Mathf.Clamp(dm.m_districts.m_buffer[0].GetLandValue() / RefLand * Housing.MarketFactor, MinWealth, MaxWealth);
            index = Mathf.Clamp(1f + Staff * (wealth - 1f) * ramp, MinIndex, MaxIndex);
            float rent = Housing.AverageRent();
            if (rent > 0f) pay += (Mathf.Clamp(1f + WageFollow * (rent / Housing.RefRent - 1f), 1f, WageMax) - pay) * WageEase;
        }

        internal static int Apply(int cost) => cost <= 0 || !Sim.Active || !Settings.Payroll.value ? cost : Mathf.CeilToInt(cost * index);
    }

    static class Wear
    {
        const float GameYear = 213722f, Grace = 15f, Life = 45f, CostMax = 0.75f, RateMin = 0.5f, RateMax = 2f;
        const int Segments = 36864, Unit = 64, PassFrames = 256, Version = 1;
        static readonly uint[] wear = new uint[Segments], stamp = new uint[Segments];
        [ThreadStatic] internal static ushort Current;
        static bool seed;
        static uint seedBuild;
        static float seedYears;
        static int seedPasses;

        internal static void Start()
        {
            if (!seed) return;
            SimulationManager sm = Singleton<SimulationManager>.instance;
            seedBuild = sm.m_currentBuildIndex;
            DateTime founded = sm.m_metaData != null ? sm.m_metaData.m_startingDateTime : new DateTime(0L);
            seedYears = founded.Ticks == 0 ? 0f : Mathf.Max(0f, (float)((sm.m_currentGameTime - founded).TotalDays / 365.25));
            seedPasses = 2;
            if (seedBuild == 0 || seedYears <= 0f) seed = false;
        }

        internal static void EndPass()
        {
            if (seed && --seedPasses <= 0) seed = false;
        }

        static uint Estimate(uint build)
        {
            if (!seed || build == 0 || build > seedBuild) return 0;
            float age = (1f - build / (float)seedBuild) * seedYears;
            return (uint)Mathf.Min(uint.MaxValue / 2f, age * GameYear / Unit);
        }

        internal static void Tick(int i, ref NetSegment s, NetInfo info)
        {
            if (stamp[i] != s.m_buildIndex)
            {
                stamp[i] = s.m_buildIndex;
                wear[i] = Estimate(s.m_buildIndex);
            }
            if (!(info.m_netAI is PlayerNetAI) || info.m_class == null) return;
            int budget = Singleton<EconomyManager>.instance.GetBudget(info.m_class);
            float rate = Mathf.Clamp(100f / Mathf.Max(1, budget), RateMin, RateMax);
            uint add = (uint)Mathf.RoundToInt(PassFrames / (float)Unit * rate);
            if (wear[i] < uint.MaxValue - add) wear[i] += add;
        }

        static float Worn(int i)
        {
            float years = wear[i] * (float)Unit / GameYear;
            float w = Mathf.Clamp01((years - Grace) / (Life - Grace));
            return w * w * (3f - 2f * w);
        }

        static bool Fresh(int i) => stamp[i] == Singleton<NetManager>.instance.m_segments.m_buffer[i].m_buildIndex;

        internal static int Apply(int cost)
        {
            ushort seg = Current;
            if (seg == 0 || cost <= 0 || !Sim.Active || !Settings.Ageing.value || !Fresh(seg)) return cost;
            return Mathf.CeilToInt(cost * (1f + CostMax * Worn(seg)));
        }

        internal static byte[] Save()
        {
            using (var ms = new System.IO.MemoryStream())
            using (var w = new System.IO.BinaryWriter(ms))
            {
                w.Write(Version);
                int count = 0;
                for (int i = 0; i < Segments; i++) if (wear[i] != 0) count++;
                w.Write(count);
                for (int i = 0; i < Segments; i++)
                {
                    if (wear[i] == 0) continue;
                    w.Write((ushort)i);
                    w.Write(stamp[i]);
                    w.Write(wear[i]);
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        internal static void Load(byte[] data)
        {
            Array.Clear(wear, 0, Segments);
            Array.Clear(stamp, 0, Segments);
            seed = true;
            if (data == null || data.Length < 8) return;
            using (var r = new System.IO.BinaryReader(new System.IO.MemoryStream(data)))
            {
                if (r.ReadInt32() != Version) return;
                seed = false;
                int count = r.ReadInt32();
                for (int n = 0; n < count && r.BaseStream.Position + 10 <= r.BaseStream.Length; n++)
                {
                    int i = r.ReadUInt16();
                    uint st = r.ReadUInt32(), wr = r.ReadUInt32();
                    if (i < Segments) { stamp[i] = st; wear[i] = wr; }
                }
            }
        }
    }

    sealed class Household
    {
        internal const byte NoSlot = 255;
        public readonly uint[] Members = new uint[5];
        public int Count;
        public bool Staying, Car;
        public ushort Shelter, Tent;
        public byte Slot = NoSlot;
        public bool Struck;
        public uint Since, Next, Charged;
        public Vector3 Origin;
    }

    struct Site
    {
        public Vector3 Pos;
        public uint Until;
    }

    sealed class Tally
    {
        public int Households, Vacant, Notices, Public, Counted, Rich, Super;
        public double Rent, Income, Shares;

        public void Clear()
        {
            Households = Vacant = Notices = Public = Counted = Rich = Super = 0;
            Rent = Income = Shares = 0;
        }

        public void CopyTo(Tally t)
        {
            t.Households = Households; t.Vacant = Vacant; t.Notices = Notices; t.Public = Public; t.Counted = Counted; t.Rich = Rich; t.Super = Super;
            t.Rent = Rent; t.Income = Income; t.Shares = Shares;
        }
    }

    struct HouseholdView
    {
        public int People, Income, Tier;
        public float Share;
        public bool Owner;
        public bool Notice;
    }

    struct Pending
    {
        public double Amount;
        public int Count;
        public uint Until;
    }

    public static class HomelessApi
    {
        public static bool Active() => Sim.Active && Settings.Homeless.value;
        public static bool IsHomeless(uint citizen) => Active() && Housing.Tracked(citizen);
        public static void Left(uint citizen) { if (Sim.Active) Housing.Left(citizen); }
    }

    static class Housing
    {
        internal static readonly int[] Wage = { 100, 150, 220, 320 };
        static readonly float[] Quality = { 0.75f, 1f, 1.3f, 1.65f, 2f };
        const float MarketLow = 0.85f, MarketHigh = 1.25f, VacancyTight = 0.02f, VacancyLoose = 0.12f, MarketWindow = 4f;
        static float marketFactor = 1f, emaHomes, emaVacant;
        static int homeBuildings, homeBuildingsAcc;
        static readonly int[] Utility = { 20, 25, 30, 35, 40 };
        const int Benefit = 60, WealthyIncome = 500, PoorIncome = 220, RichChance = 4, SuperChance = 1;
        const float RentShare = 0.3f, RentFloor = 0.6f, RentSpread = 0.9f, LandScale = 60f, LandWeight = 0.75f, LeaseStep = 0.1f, JobStep = 0.15f, ExpectedJob = 1.15f, StudentShare = 0.5f, TeenShare = 0.6f;
        const float LowDensity = 1.3f, MoveInShare = 0.4f, StayShare = 0.6f, Grandfather = 0.95f, CatchUp = 0.4f, BuyFactor = 1.15f;
        const uint OwnerShare = 70;
        const int ForecloseAfter = 5, LongUnemployed = 5, DropNotice = 35, DropSqueeze = 10;
        internal const float RefRent = RentShare * 150f * (RentFloor + RentSpread * 0.5f);
        const float RentTax = 2f, WageTax = 1.5f, PublicMarket = 0.5f, RentCents = 3f, ShelterCents = 120f, WeekFrames = 4096f;
        internal const float MoneyPerPoint = RentCents / 100f;
        internal const int CityTally = 128, Buckets = 200, BucketSize = 10;
        const uint SearchFrames = 8192, SearchInterval = 512, StayInterval = 4096, SiteFrames = 8192;
        const int StayPoor = 70, StayMiddle = 35, JobLoss = 30, ShelterCells = 10, AnchorCapacity = 3, CarIncome = 300, StreetCrime = 5, CarCrime = 3, CrimeDivisor = 5, CrimeCap = 100, Notices = 4096, TentSlots = 6, PressureThreshold = 12;
        internal const int SlowGrowth = 50;
        const float CompAdult = 60f, CompChild = 90f, CompSenior = 110f, CompEducation = 0.35f, CompLevel = 0.4f;
        const float WalkHome = 500f, SiteRadius = 200f, SitePenalty = 25f, PresencePerHousehold = 3f, PresenceMax = 30f;
        const float ControlBase = 3f, ControlGap = 7f, ControlFullGap = 0.25f, SubsidyBonus = 8f, TentPenalty = 10f, TentPenaltyMax = 25f, TentWorker = 5f, ParkLoss = 0.15f, ParkFloor = 0.25f;
        const float PrimeStart = 1.25f, PrimeFull = 2f, PrimeMax = 1.5f, VacantHigh = 0.25f, VacantLow = 0.1f;
        const int DiscountStep = 10, DiscountRecover = 5, DiscountMax = 50, Units = 524288, Districts = 128;
        internal const int Buildings = 49152;
        const int Citizens = 1048576, Cells = 270 * 270, VacancySlots = 1024, PublicSlots = 256, PoolSize = 64, VacancyPerFrame = Buildings / 4096;
        internal const byte PublicFlag = 1, ShelterFlag = 2, SeenFlag = 4, GrandFlag = 8, ControlBit = 1, SubsidyBit = 2, CriminalBit = 4, PublicBit = 64, BanBit = 128;
        internal const byte Unrolled = 0, Normal = 1, Rich = 2, Super = 3, NoticeBurden = 255;

        static readonly object sync = new object();
        static readonly List<Household> households = new List<Household>();
        static readonly Dictionary<uint, Household> byCitizen = new Dictionary<uint, Household>();
        static readonly Dictionary<ushort, int> occupancy = new Dictionary<ushort, int>(), tentsAt = new Dictionary<ushort, int>();
        static readonly Dictionary<ushort, byte> slotMask = new Dictionary<ushort, byte>();
        static readonly Dictionary<ushort, List<uint>> extra = new Dictionary<ushort, List<uint>>();
        static readonly Dictionary<ushort, Pending> pending = new Dictionary<ushort, Pending>();
        static readonly List<Site> sites = new List<Site>();
        static readonly Dictionary<uint, uint> notices = new Dictionary<uint, uint>();
        static readonly List<uint> due = new List<uint>(), unitTmp = new List<uint>();
        static readonly List<int> incomeTmp = new List<int>(), tierTmp = new List<int>();
        static readonly ushort[] rent = new ushort[Buildings], market = new ushort[Buildings];
        static readonly byte[] flags = new byte[Buildings], pressure = new byte[Buildings], burden = new byte[Buildings], pension = new byte[Citizens], tier = new byte[Citizens];
        static readonly byte[] policies = new byte[128], discount = new byte[Buildings];
        static readonly uint[] movedIn = new uint[Units], owner = new uint[Units];
        static readonly int[] districtPublicAcc = new int[Districts], districtAllAcc = new int[Districts];
        static readonly float[] districtPublic = new float[Districts];
        static readonly Tally[] tallying = NewTallies(), tallied = NewTallies();
        static readonly int[] histogram = new int[Buckets], finalHistogram = new int[Buckets];
        internal static int StreetHomeless, CarHomeless, Sheltered, StayingHomeless, JailedHomeless;
        static float meanDesirability = 0.4f;
        static double desirabilitySum;
        static int desirabilityCount;
        static readonly ushort[] vacBuilding = new ushort[VacancySlots], pubBuilding = new ushort[PublicSlots];
        static readonly uint[] vacUnit = new uint[VacancySlots], pubUnit = new uint[PublicSlots];
        static readonly uint[] poolUnit = new uint[PoolSize];
        static readonly int[] poolIncome = new int[PoolSize];
        static readonly float[] presence = new float[Cells], streets = new float[Cells], camp = new float[Cells];
        static readonly byte[] doorstep = new byte[Buildings], alarm = new byte[Buildings];
        static readonly HashSet<int> camps = new HashSet<int>();
        static readonly int[] nx, nz;
        static readonly float[] nw;
        const Notification.Problem1 DoorIcon = Notification.Problem1.TooFewServices;
        const float DoorStart = 4f, DoorRed = 9f, DoorTent = 0.5f, DoorLone = 0.25f, Gather = 0.75f, GatherCap = 8f, CampSize = 6f, CampGone = 3f;
        internal const int DoorMinor = 48, DoorMajor = 200;
        static readonly uint[] scratch = new uint[5];
        static readonly int[] px, pz;
        static readonly float[] pw;
        static int vacNext, pubNext, poolCount, rentCursor, vacancyCursor, householdCursor, publicAcc, allAcc;
        static float publicShare;
        static uint graceUntil, pendingUnit;
        static byte pendingTier;
        static bool loaded;
        internal static ushort Current;
        internal static bool InRemove;
        static bool anyControl, anyPublic;

        static Housing()
        {
            Walk.Kernel(3, out px, out pz, out pw);
            Walk.Kernel(1, out nx, out nz, out nw);
        }

        static Tally[] NewTallies()
        {
            var t = new Tally[Districts + 1];
            for (int i = 0; i < t.Length; i++) t[i] = new Tally();
            return t;
        }

        internal static Tally Stats(int district) => tallied[district];
        internal static int[] Histogram => finalHistogram;
        internal static float MeanDesirability => meanDesirability;

        static Building[] B => Singleton<BuildingManager>.instance.m_buildings.m_buffer;
        static Citizen[] C => Singleton<CitizenManager>.instance.m_citizens.m_buffer;
        static CitizenUnit[] U => Singleton<CitizenManager>.instance.m_units.m_buffer;

        internal static void Start(uint frame)
        {
            lock (sync)
            {
                if (!loaded) graceUntil = frame + Sim.YearFrames;
                loaded = false;
                occupancy.Clear();
                tentsAt.Clear();
                slotMask.Clear();
                Tents.Clear();
                var pitch = new List<Household>();
                foreach (Household h in households)
                {
                    if (h.Shelter != 0 && (B[h.Shelter].m_flags & (Building.Flags.Created | Building.Flags.Deleted)) != Building.Flags.Created) h.Shelter = 0;
                    if (h.Shelter != 0) occupancy[h.Shelter] = Occupied(h.Shelter) + 1;
                    if (h.Tent != 0)
                    {
                        Tents.Discard(h.Tent);
                        h.Tent = 0;
                        if (h.Shelter != 0 && !h.Car) pitch.Add(h);
                    }
                    h.Slot = Household.NoSlot;
                    h.Next = h.Charged = frame;
                }
                if (Settings.Tents.value) foreach (Household h in pitch) Pitch(h, h.Shelter);
                RefreshControl();
            }
        }

        internal static void Clear()
        {
            lock (sync)
            {
                households.Clear();
                byCitizen.Clear();
                occupancy.Clear();
                tentsAt.Clear();
                slotMask.Clear();
                extra.Clear();
                pending.Clear();
                sites.Clear();
                notices.Clear();
                foreach (Array a in new Array[] { rent, market, flags, pressure, burden, pension, tier, policies, discount, movedIn, owner, districtPublicAcc, districtAllAcc, districtPublic, vacUnit, pubUnit, presence, streets, camp, doorstep, alarm, histogram, finalHistogram, living, landDrop, landPeak }) Array.Clear(a, 0, a.Length);
                seeking.Clear();
                seekList.Clear();
                seekCursor = 0;
                camps.Clear();
                for (int d = 0; d <= Districts; d++) { tallying[d].Clear(); tallied[d].Clear(); }
                StreetHomeless = CarHomeless = Sheltered = StayingHomeless = JailedHomeless = 0;
                meanDesirability = 0.4f;
                marketFactor = 1f;
                emaHomes = emaVacant = 0f;
                homeBuildings = homeBuildingsAcc = 0;
                desirabilitySum = 0;
                desirabilityCount = 0;
                Community.Clear();
                vacNext = pubNext = poolCount = rentCursor = vacancyCursor = householdCursor = publicAcc = allAcc = 0;
                publicShare = 0f;
                graceUntil = pendingUnit = 0;
                loaded = anyControl = anyPublic = InRemove = false;
                Current = 0;
            }
        }

        internal static void Step(uint frame)
        {
            lock (sync)
            {
                for (int i = sites.Count - 1; i >= 0; i--) if ((int)(frame - sites[i].Until) >= 0) sites.RemoveAt(i);
                RentPass(frame);
                VacancyPass();
                if (Settings.Homeless.value || Settings.Rent.value) PublicPass();
                if (Settings.Rent.value) SeekPass(frame);
                if ((frame & 255) == 0) RebuildPresence();
                if ((frame & 63) == 0 && notices.Count > 0) NoticePass(frame);
                if (households.Count > 0) HouseholdPass(frame);
                Community.Step(frame);
            }
        }

        static void Count(Dictionary<ushort, int> d, ushort id, int delta)
        {
            int n = (d.TryGetValue(id, out int v) ? v : 0) + delta;
            if (n > 0) d[id] = n;
            else d.Remove(id);
        }

        static byte Mask(ushort id) => slotMask.TryGetValue(id, out byte m) ? m : (byte)0;

        internal static bool IsHome(ushort id)
        {
            if ((B[id].m_flags & (Building.Flags.Created | Building.Flags.Completed | Building.Flags.Deleted | Building.Flags.Abandoned | Building.Flags.Collapsed)) != (Building.Flags.Created | Building.Flags.Completed)) return false;
            BuildingInfo info = B[id].Info;
            return info != null && info.m_class.m_service == ItemClass.Service.Residential;
        }

        internal static bool IsResidential(ushort id)
        {
            BuildingInfo info = B[id].Info;
            return info != null && info.m_class.m_service == ItemClass.Service.Residential;
        }

        internal static bool IsPublic(ushort id) => (flags[id] & PublicFlag) != 0 || anyPublic && Forced(id);
        internal static bool MarkedPublic(ushort id) => (flags[id] & PublicFlag) != 0;
        internal static bool Forced(ushort id) => (flags[id] & (PublicFlag | ShelterFlag)) == 0 && (PolicyAt(B[id].m_position) & PublicBit) != 0 && IsResidential(id);
        internal static bool IsShelter(ushort id) => (flags[id] & ShelterFlag) != 0;
        internal static bool HasPolicy(byte district, byte bit) => (policies[district] & bit) != 0;
        internal static byte PolicyAt(Vector3 p) => (byte)(policies[Singleton<DistrictManager>.instance.GetDistrict(p)] | policies[0]);
        internal static bool Criminalized(Vector3 p) => (PolicyAt(p) & CriminalBit) != 0;
        internal static bool Raid(ushort id) => id != 0 && households.Count > 0 && Occupied(id) > 0 && !IsShelter(id) && Criminalized(B[id].m_position);
        internal static bool Controlled(ushort id) => anyControl && (PolicyAt(B[id].m_position) & ControlBit) != 0;
        internal static bool ControlledAt(Vector3 p) => anyControl && (PolicyAt(p) & ControlBit) != 0;
        internal static int TentsAt(ushort id) => tentsAt.Count > 0 && tentsAt.TryGetValue(id, out int n) ? n : 0;
        internal static byte Burden(ushort id) => burden[id];
        internal static float PresenceAt(Vector3 p) => households.Count > 0 ? Walk.Sum(presence, p, px, pz, pw) : 0f;
        internal static bool Voteless(ushort home) => home == 0 || IsShelter(home) || !IsHome(home);

        static void RefreshControl()
        {
            byte all = 0;
            foreach (byte b in policies) all |= b;
            anyControl = (all & ControlBit) != 0;
            anyPublic = (all & PublicBit) != 0;
        }

        internal static void SetPolicy(byte district, byte bit, bool on)
        {
            lock (sync)
            {
                policies[district] = (byte)(on ? policies[district] | bit : policies[district] & ~bit);
                RefreshControl();
            }
        }

        internal static void SetPublic(ushort id, bool on)
        {
            if (on && IsShelter(id)) SetShelter(id, false);
            lock (sync) flags[id] = (byte)(on ? flags[id] | PublicFlag : flags[id] & ~PublicFlag);
        }

        internal static void SetShelter(ushort id, bool on)
        {
            lock (sync)
            {
                if (on) flags[id] = (byte)(flags[id] & ~PublicFlag);
                flags[id] = (byte)(on ? flags[id] | ShelterFlag : flags[id] & ~ShelterFlag);
                if (on) return;
                Citizen[] z = C;
                foreach (Household h in households)
                {
                    if (h.Shelter != id) continue;
                    for (int i = 0; i < h.Count; i++) if (z[h.Members[i]].m_homeBuilding == id) z[h.Members[i]].SetHome(h.Members[i], 0, 0u);
                    h.Next = Sim.Frame;
                }
                ReleaseExtra(id);
            }
        }

        static int Earned(ref Citizen data, ushort work)
        {
            float wage = Wage[Mathf.Clamp((int)data.EducationLevel, 0, 3)];
            BuildingInfo info = B[work].Info;
            if (info == null) return (int)wage;
            wage *= 1f + JobStep * Mathf.Clamp(B[work].m_level, 0, 4);
            ItemClass.Service s = info.m_class.m_service;
            bool taxed = s == ItemClass.Service.Commercial || s == ItemClass.Service.Office || s == ItemClass.Service.Industrial;
            if (taxed) wage *= Mathf.Max(0.5f, 1f - WageTax * (Singleton<EconomyManager>.instance.GetTaxRate(info.m_class) - 9) / 100f);
            if (taxed || s == ItemClass.Service.PlayerIndustry || s == ItemClass.Service.Fishing) wage *= Payroll.Pay;
            else if (Settings.Payroll.value) wage *= Mathf.Max(1f, Payroll.Index);
            return (int)wage;
        }

        internal static int CitizenIncome(uint c)
        {
            Citizen[] z = C;
            if ((z[c].m_flags & Citizen.Flags.Created) == 0 || z[c].Dead) return 0;
            int education = Mathf.Clamp((int)z[c].EducationLevel, 0, 3);
            switch (Citizen.GetAgeGroup(z[c].Age))
            {
                case Citizen.AgeGroup.Child: return 0;
                case Citizen.AgeGroup.Teen: return z[c].m_workBuilding != 0 && (z[c].m_flags & Citizen.Flags.Student) == 0 ? (int)(Earned(ref z[c], z[c].m_workBuilding) * TeenShare) : 0;
                case Citizen.AgeGroup.Senior:
                    int saved = (pension[c] & 0x7F) * 5;
                    return saved > 0 ? saved : Wage[education];
            }
            if ((z[c].m_flags & Citizen.Flags.Student) != 0) return Math.Max(Benefit, (int)(Wage[education] * StudentShare));
            ushort work = z[c].m_workBuilding;
            return work == 0 ? Benefit : Earned(ref z[c], work);
        }

        static int UnitIncome(uint unit)
        {
            int sum = 0;
            for (int i = 0; i < 5; i++)
            {
                uint c = U[unit].GetCitizen(i);
                if (c != 0) sum += CitizenIncome(c);
            }
            return sum;
        }

        static int UnitTier(uint unit)
        {
            int t = Unrolled;
            for (int i = 0; i < 5; i++)
            {
                uint c = U[unit].GetCitizen(i);
                if (c != 0) t = Math.Max(t, tier[c]);
            }
            return t;
        }

        static uint HomeUnit(uint c, ref Citizen data) => data.m_homeBuilding == 0 ? 0u : B[data.m_homeBuilding].FindCitizenUnit(CitizenUnit.Flags.Home, c);

        internal static int HouseholdIncome(uint c, ref Citizen data)
        {
            uint unit = HomeUnit(c, ref data);
            return unit != 0 ? UnitIncome(unit) : CitizenIncome(c);
        }

        internal static float AverageRent()
        {
            Tally t = tallied[CityTally].Households > 0 ? tallied[CityTally] : tallying[CityTally];
            int renters = t.Households - t.Public;
            return renters >= 50 ? (float)(t.Rent / renters) : 0f;
        }

        internal static int Strain(uint c, ref Citizen data)
        {
            if (!Settings.Rent.value) return 0;
            ushort home = data.m_homeBuilding;
            if (home == 0 || !IsHome(home) || IsShelter(home) || IsPublic(home)) return 0;
            uint unit = HomeUnit(c, ref data);
            if (unit == 0 || Exempt(UnitTier(unit))) return 0;
            if (notices.ContainsKey(unit)) return 2;
            if (IsLow(home) && Owner(unit)) return 0;
            int income = UnitIncome(unit);
            float squeeze = (income > 0 ? TenantRent(home, PolicyAt(B[home].m_position)) / income : 2f) + living[home] / 100f + Heating(home);
            return squeeze > SpendStart ? 1 : 0;
        }

        [ThreadStatic] internal static int Hustle;

        internal static int Hunt(uint c, ref Citizen data)
        {
            if (Strain(c, ref data) > 0) return 3;
            return data.Unemployed >= LongUnemployed && Sim.Roll(3) == 0 ? 2 : 0;
        }

        internal static void Offer(TransferManager tm, TransferManager.TransferReason reason, TransferManager.TransferOffer offer)
        {
            int h = Hustle;
            if (h != 0 && offer.Citizen != 0)
            {
                if ((h & 1) != 0 && (reason == TransferManager.TransferReason.Student2 || reason == TransferManager.TransferReason.Student3)) return;
                if ((h & 2) != 0 && reason > TransferManager.TransferReason.Worker0 && reason <= TransferManager.TransferReason.Worker3)
                {
                    tm.AddOutgoingOffer(reason, offer);
                    offer.Priority = Mathf.Max(0, offer.Priority - 2);
                    tm.AddOutgoingOffer((TransferManager.TransferReason)((int)reason - 1 - Sim.Roll((uint)(reason - TransferManager.TransferReason.Worker0))), offer);
                    return;
                }
            }
            Religion.StudentOffer(tm, reason, offer);
        }

        internal static void DropOut(uint c, ref Citizen data)
        {
            if ((data.m_flags & Citizen.Flags.Student) == 0 || data.m_workBuilding == 0 || data.CurrentLocation != Citizen.Location.Home) return;
            Citizen.AgeGroup age = Citizen.GetAgeGroup(data.Age);
            if (age == Citizen.AgeGroup.Child || age == Citizen.AgeGroup.Senior) return;
            int strain = Strain(c, ref data);
            if (strain == 0 || Sim.Roll(100) >= (strain == 2 ? DropNotice : DropSqueeze)) return;
            data.SetWorkplace(c, 0, 0u);
            if (age != Citizen.AgeGroup.Teen) data.Unemployed = 1;
        }

        internal static int HouseholdTier(uint c, ref Citizen data)
        {
            uint unit = HomeUnit(c, ref data);
            return unit != 0 ? UnitTier(unit) : tier[c];
        }

        static byte RollTier()
        {
            int r = Sim.Roll(100);
            return r < SuperChance ? Super : r < SuperChance + RichChance ? Rich : Normal;
        }

        internal static int Tier(uint c) => tier[c];

        internal static void Tier(uint c, ref Citizen data)
        {
            if (tier[c] != Unrolled) return;
            byte t = Unrolled;
            uint unit = HomeUnit(c, ref data);
            for (int i = 0; unit != 0 && i < 5 && t == Unrolled; i++)
            {
                uint o = U[unit].GetCitizen(i);
                if (o != 0 && o != c) t = tier[o];
            }
            tier[c] = t != Unrolled ? t : RollTier();
        }

        static bool Covers(int t, ushort id) => t == Super || (t == Rich && B[id].m_level <= 3);
        static bool Exempt(int t) => t >= Rich;

        const float SpendStart = 0.35f, SpendFull = 0.75f, SpendFloor = 0.6f;
        const float PriceSlope = 0.25f, PriceLow = 0.9f, PriceHigh = 1.2f, GoodsShare = 0.3f, HeatStart = 8f, HeatRange = 20f, HeatMax = 0.06f;
        const float ChoosyStart = 0.2f, ChoosyMax = 0.6f, SeekCut = 0.85f;
        const float StrRent = 0.8f, StrBlock = 2f, StrTax = 0.3f, StrCap = 0.5f;
        const float ColdStart = 10f, ColdRange = 30f, HotStart = 32f, HotPivot = 34f, HotBase = 0.08f, HotGrowth = 1.38f, CarCold = 0.35f, CarHot = 0.5f, SquatExposure = 0.5f;
        const int ExposureDeath = 30, ExposureSick = 200, SeekMax = 1024, SeekPerFrame = 4;
        const float OwnerAnger = 20f, AngerStart = 0.1f, AngerRange = 0.3f, PeakDecay = 1f / 96f;
        static readonly sbyte[] living = new sbyte[Buildings];
        static readonly byte[] landDrop = new byte[Buildings];
        static readonly float[] landPeak = new float[Buildings];
        static readonly Dictionary<uint, Seek> seeking = new Dictionary<uint, Seek>();
        static readonly List<uint> seekList = new List<uint>();
        static int seekCursor;
        static readonly List<uint> expired = new List<uint>();
        [ThreadStatic] internal static bool Picky;

        struct Seek
        {
            public int Limit;
            public uint Until;
        }

        static float Temperature => Singleton<WeatherManager>.instance.m_currentTemperature;

        static float ColdRisk()
        {
            float t = Temperature;
            return t >= ColdStart ? 0f : Mathf.Min(1f, Mathf.Pow((ColdStart - t) / ColdRange, 1.5f));
        }

        static float HeatRisk()
        {
            float t = Temperature;
            return t <= HotStart ? 0f : Mathf.Min(1f, HotBase * Mathf.Pow(HotGrowth, t - HotPivot));
        }

        static float Heating(ushort id) => B[id].m_heatingBuffer > 0 ? 0f : HeatMax * Mathf.Clamp01((HeatStart - Temperature) / HeatRange);

        internal static float ShortStay(ushort id)
        {
            if ((flags[id] & (PublicFlag | ShelterFlag)) != 0) return 0f;
            Vector3 p = B[id].m_position;
            if ((PolicyAt(p) & BanBit) != 0 || anyPublic && Forced(id)) return 0f;
            Singleton<ImmaterialResourceManager>.instance.CheckLocalResource(ImmaterialResourceManager.Resource.Attractiveness, p, out int attraction);
            float max = Settings.HolidayShare.value / 100f;
            return max <= 0f ? 0f : max * Walk.Sat(attraction, Mathf.Max(1f, Settings.HolidayAttraction.value));
        }

        internal static bool Choose(int income, int t, ushort id)
        {
            Picky = false;
            if (!Affordable(income, t, id)) return false;
            float accept = 1f - Mathf.Min(0.9f, ShortStay(id) * StrBlock);
            if (!Covers(t, id))
            {
                float price = IsLow(id) ? Rent(id) * BuyFactor : TenantRent(id, PolicyAt(B[id].m_position));
                float share = income > 0 ? price / income : 1f;
                accept *= 1f - ChoosyMax * Mathf.Clamp01((share - ChoosyStart) / (MoveInShare - ChoosyStart));
            }
            if (accept >= 1f || Sim.Roll(1000u) < accept * 1000f) return true;
            Picky = true;
            return false;
        }

        internal static bool WithinBudget(uint citizenID, ref Citizen data, ushort target)
        {
            if (seeking.Count == 0 || data.m_homeBuilding == 0) return true;
            uint unit = data.GetContainingUnit(citizenID, B[data.m_homeBuilding].m_citizenUnits, CitizenUnit.Flags.Home);
            if (unit == 0 || !seeking.TryGetValue(unit, out Seek seek)) return true;
            if ((int)(Sim.Frame - seek.Until) >= 0) { seeking.Remove(unit); return true; }
            float price = IsLow(target) ? Rent(target) * BuyFactor : TenantRent(target, PolicyAt(B[target].m_position));
            return price <= seek.Limit;
        }

        static float Price(ushort id) => IsLow(id) ? Rent(id) * BuyFactor : TenantRent(id, PolicyAt(B[id].m_position));

        static bool Comfortable(int income, int t, ushort id, float price) => Covers(t, id) || income > 0 && price / income + living[id] / 100f + Heating(id) <= SpendStart;

        static int Vacancy(int income, int t, Vector3 near, float cheaper, ushort skip)
        {
            int comfy = -1, cheap = -1;
            float near2 = float.MaxValue, low = float.MaxValue;
            for (int i = 0; i < VacancySlots; i++)
            {
                if (vacUnit[i] == 0) continue;
                ushort id = vacBuilding[i];
                if (!FreeUnit(id, vacUnit[i]) || IsPublic(id)) { vacUnit[i] = 0; continue; }
                if (id == skip) continue;
                float price = Price(id);
                if (Comfortable(income, t, id, price))
                {
                    float d = VectorUtils.LengthSqrXZ(B[id].m_position - near);
                    if (d < near2) { near2 = d; comfy = i; }
                }
                else if (comfy < 0 && price < cheaper && price < low && income * MoveInShare >= price) { low = price; cheap = i; }
            }
            return comfy >= 0 ? comfy : cheap;
        }

        static void MoveInto(int slot, int n)
        {
            uint target = vacUnit[slot];
            vacUnit[slot] = 0;
            Citizen[] z = C;
            for (int i = 0; i < n; i++) z[scratch[i]].SetHome(scratch[i], 0, target);
        }

        static bool Relocate(uint unit, ushort from, float cheaper)
        {
            int slot = Vacancy(UnitIncome(unit), UnitTier(unit), B[from].m_position, cheaper, from);
            if (slot < 0) return false;
            int n = Collect(unit);
            if (n == 0) return false;
            MoveInto(slot, n);
            seeking.Remove(unit);
            notices.Remove(unit);
            return true;
        }

        internal static bool Settle(uint c, ref Citizen data, ushort target, bool family, int income, int t)
        {
            float price = Price(target);
            if (Comfortable(income, t, target, price)) return true;
            int slot = Vacancy(income, t, B[target].m_position, price, target);
            if (slot < 0) return true;
            uint home = family ? HomeUnit(c, ref data) : 0u;
            int n = home != 0 ? Collect(home) : 0;
            if (n == 0) { scratch[0] = c; n = 1; }
            MoveInto(slot, n);
            if (home != 0) { seeking.Remove(home); notices.Remove(home); }
            return false;
        }

        static void Shop(uint unit, float tenant, uint frame)
        {
            if (Relocate(unit, U[unit].m_building, tenant)) return;
            bool known = seeking.ContainsKey(unit);
            if (!known && seeking.Count >= SeekMax) return;
            seeking[unit] = new Seek { Limit = Mathf.FloorToInt(tenant * SeekCut), Until = frame + Sim.YearFrames };
            if (!known) seekList.Add(unit);
            CitizenUnit[] u = U;
            Citizen[] z = C;
            uint head = 0;
            int size = 0;
            for (int i = 4; i >= 0; i--)
            {
                uint c = u[unit].GetCitizen(i);
                if (c != 0 && !z[c].Dead) { size++; head = c; }
            }
            if (head != 0 && z[head].GetCitizenInfo(head)?.m_citizenAI is ResidentAI ai) ai.TryMoveFamily(head, ref z[head], size);
        }

        static void SeekPass(uint frame)
        {
            CitizenUnit[] u = U;
            for (int k = 0; k < SeekPerFrame && seekList.Count > 0; k++)
            {
                if (seekCursor >= seekList.Count) seekCursor = 0;
                uint unit = seekList[seekCursor];
                bool drop = !seeking.TryGetValue(unit, out Seek seek) || (int)(frame - seek.Until) >= 0 || (u[unit].m_flags & CitizenUnit.Flags.Home) == 0 || u[unit].Empty() || !IsHome(u[unit].m_building) || IsPublic(u[unit].m_building);
                if (drop) seeking.Remove(unit);
                if (drop || Relocate(unit, u[unit].m_building, TenantRent(u[unit].m_building, PolicyAt(B[u[unit].m_building].m_position))))
                {
                    seekList[seekCursor] = seekList[seekList.Count - 1];
                    seekList.RemoveAt(seekList.Count - 1);
                    continue;
                }
                seekCursor++;
            }
        }

        static void Expire(uint frame)
        {
            if (seeking.Count == 0) return;
            expired.Clear();
            foreach (var kv in seeking) if ((int)(frame - kv.Value.Until) >= 0) expired.Add(kv.Key);
            foreach (uint unit in expired) seeking.Remove(unit);
        }

        internal static void LandWatch(ushort id, ref Building b)
        {
            if (!IsLow(id)) return;
            Singleton<ImmaterialResourceManager>.instance.CheckLocalResource(ImmaterialResourceManager.Resource.LandValue, b.m_position, out int land);
            float peak = landPeak[id];
            peak = peak <= 0f || land >= peak ? land : peak - (peak - land) * PeakDecay;
            landPeak[id] = peak;
            float drop = peak > 0f ? (peak - land) / peak : 0f;
            landDrop[id] = (byte)Mathf.Clamp(Mathf.RoundToInt(drop * 100f), 0, 100);
            if (drop > AngerStart + AngerRange * 0.5f && Sim.Roll(4000u) == 0) Chirps.Send("ValueDrop", 0);
        }

        static float Anger(ushort home) => Mathf.Clamp01((landDrop[home] / 100f - AngerStart) / AngerRange);

        internal static float OwnerLoss(uint citizen, ushort home)
        {
            if (home == 0 || landDrop[home] == 0 || !IsLow(home)) return 0f;
            uint unit = C[citizen].GetContainingUnit(citizen, B[home].m_citizenUnits, CitizenUnit.Flags.Home);
            return unit != 0 && Owner(unit) ? 0.7f * Anger(home) : 0f;
        }

        internal static float Spending(ushort home, uint unit)
        {
            if (!Settings.Rent.value || home == 0 || !IsHome(home) || IsShelter(home)) return 1f;
            if (unit != 0 && Exempt(UnitTier(unit))) return 1f;
            bool pub = IsPublic(home);
            float squeeze = living[home] / 100f + (pub ? 0f : Heating(home));
            if (!pub && !(unit != 0 && IsLow(home) && Owner(unit)))
            {
                byte b = burden[home];
                squeeze += b == NoticeBurden ? 1f : b / 100f;
            }
            return Mathf.Lerp(1f, SpendFloor, (squeeze - SpendStart) / (SpendFull - SpendStart));
        }

        internal static void Goods(uint unit, ref CitizenUnit data)
        {
            float f = Spending(data.m_building, unit);
            if (f >= 1f) return;
            float save = 20f * (1f - f);
            int whole = (int)save;
            if (Sim.Roll(1000u) < (save - whole) * 1000f) whole++;
            if (whole > 0) data.m_goods = (ushort)Math.Min(ushort.MaxValue, data.m_goods + whole);
        }

        internal static bool IsLeisure(TransferManager.TransferReason r) => r == TransferManager.TransferReason.Entertainment || r == TransferManager.TransferReason.EntertainmentB || r == TransferManager.TransferReason.EntertainmentC || r == TransferManager.TransferReason.EntertainmentD;

        internal static bool Outing(uint citizenID, TransferManager.TransferReason reason)
        {
            if (!Settings.Rent.value || !IsLeisure(reason)) return true;
            ref Citizen c = ref C[citizenID];
            ushort home = c.m_homeBuilding;
            if (home == 0 || (c.m_flags & Citizen.Flags.Tourist) != 0 || Exempt(tier[citizenID])) return true;
            uint unit = IsLow(home) ? c.GetContainingUnit(citizenID, B[home].m_citizenUnits, CitizenUnit.Flags.Home) : 0u;
            float f = Spending(home, unit);
            if (f >= 1f || Sim.Roll(1000u) < f * 1000f) return true;
            if (Sim.Roll(3000u) == 0) Chirps.Send("Budget", citizenID);
            return false;
        }

        static int MarketRent(ushort id) => MarketRent(id, out float _);

        static int MarketRent(ushort id, out float desirability)
        {
            Vector3 p = B[id].m_position;
            BuildingInfo info = B[id].Info;
            Singleton<ImmaterialResourceManager>.instance.CheckLocalResource(ImmaterialResourceManager.Resource.LandValue, p, out int land);
            int level = Mathf.Clamp(B[id].m_level, 0, 4);
            desirability = LandWeight * Walk.Sat(land, LandScale) + (1f - LandWeight) * Walk.Local(p);
            float r = RentShare * Wage[1] * Quality[level] * (RentFloor + RentSpread * desirability) * marketFactor;
            ItemClass.SubService s = info.m_class.m_subService;
            if (s == ItemClass.SubService.ResidentialLow || s == ItemClass.SubService.ResidentialLowEco) r *= LowDensity;
            r *= 1f + RentTax * (Singleton<EconomyManager>.instance.GetTaxRate(info.m_class) - 9) / 100f;
            r *= Mathf.Max(0.7f, 1f - PublicMarket * (0.5f * districtPublic[Singleton<DistrictManager>.instance.GetDistrict(p)] + 0.5f * publicShare));
            float prime = Mathf.Clamp01((desirability / Mathf.Max(0.05f, meanDesirability) - PrimeStart) / (PrimeFull - PrimeStart));
            r *= (1f + PrimeMax * prime * prime) * (1f - discount[id] / 100f) * (1f + StrRent * ShortStay(id));
            return Mathf.Clamp(Mathf.RoundToInt(r), 1, ushort.MaxValue);
        }

        internal const float MaxRent = RentShare * 150f * 2f * (RentFloor + RentSpread) * MarketHigh * LowDensity * (1f + RentTax * (29f - 9f) / 100f) * (1f + PrimeMax) * (1f + StrRent * StrCap);

        static void Market(int homes, int vacant)
        {
            float step = MarketWindow / Mathf.Max(256, homeBuildings);
            emaHomes += (homes - emaHomes) * step;
            emaVacant += (vacant - emaVacant) * step;
            if (emaHomes <= 0.01f) return;
            float mid = 0.5f * (VacancyTight + VacancyLoose), half = 0.5f * (VacancyLoose - VacancyTight);
            float vacancy = Mathf.Clamp((mid - emaVacant / emaHomes) / half, -1f, 1f);
            float demand = Mathf.Clamp((Singleton<ZoneManager>.instance.m_actualResidentialDemand + Difficulty.DemandOffset - 50) / 50f, -1f, 1f);
            float score = 0.6f * vacancy + 0.4f * demand;
            float target = score >= 0f ? 1f + (MarketHigh - 1f) * score : 1f + (1f - MarketLow) * score;
            marketFactor = Mathf.Clamp(marketFactor + (target - marketFactor) * step, MarketLow, MarketHigh);
        }

        internal static float MarketFactor => marketFactor;

        static int Rent(ushort id)
        {
            if (rent[id] == 0) rent[id] = market[id] = (ushort)MarketRent(id);
            return rent[id];
        }

        static float TenantRent(ushort id, byte policy)
        {
            if (IsPublic(id)) return 0f;
            float r = Rent(id);
            return (policy & SubsidyBit) != 0 ? r * (1f - Settings.SubsidyShare.value) : r;
        }

        internal static bool Affordable(int income, int t, ushort id) => Covers(t, id) || income * MoveInShare >= (IsLow(id) ? Rent(id) * BuyFactor : TenantRent(id, PolicyAt(B[id].m_position)));

        internal static bool IsLow(ushort id)
        {
            BuildingInfo info = B[id].Info;
            if (info == null) return false;
            ItemClass.SubService s = info.m_class.m_subService;
            return s == ItemClass.SubService.ResidentialLow || s == ItemClass.SubService.ResidentialLowEco;
        }

        static bool Owner(uint unit) => (unit * 2654435761u >> 16) % 100u < OwnerShare;

        static bool Destitute(uint unit)
        {
            Citizen[] z = C;
            bool adult = false;
            for (int i = 0; i < 5; i++)
            {
                uint c = U[unit].GetCitizen(i);
                if (c == 0 || z[c].Dead) continue;
                Citizen.AgeGroup age = Citizen.GetAgeGroup(z[c].Age);
                if (age == Citizen.AgeGroup.Child || age == Citizen.AgeGroup.Teen) continue;
                if (age == Citizen.AgeGroup.Senior || z[c].m_workBuilding != 0 || (z[c].m_flags & Citizen.Flags.Student) != 0 || z[c].Unemployed < ForecloseAfter) return false;
                adult = true;
            }
            return adult;
        }

        internal static bool AdmitImmigrant(ushort target, int education, bool family)
        {
            lock (sync)
            {
                pendingUnit = 0;
                int income = (int)(Wage[education] * ExpectedJob * Payroll.Pay * (family ? 2 : 1));
                if (Choose(income, Normal, target))
                {
                    float price = Price(target);
                    if (Comfortable(income, Normal, target, price) || Vacancy(income, Normal, B[target].m_position, price, target) < 0) return true;
                    Picky = true;
                    return false;
                }
                if (Picky) return false;
                byte t = RollTier();
                if (t == Normal || !Covers(t, target)) return false;
                pendingTier = t;
                pendingUnit = B[target].GetEmptyCitizenUnit(CitizenUnit.Flags.Home);
                return pendingUnit != 0;
            }
        }

        internal static void ImmigrantArrived()
        {
            lock (sync)
            {
                if (pendingUnit == 0) return;
                for (int i = 0; i < 5; i++)
                {
                    uint c = U[pendingUnit].GetCitizen(i);
                    if (c != 0 && tier[c] == Unrolled) tier[c] = pendingTier;
                }
                pendingUnit = 0;
            }
        }

        internal static int TaxIncome(ushort id, int amount)
        {
            if (IsPublic(id)) return 0;
            if (amount <= 0) return amount;
            int r = rent[id], m = market[id];
            if (r > 0 && m > r && Controlled(id))
            {
                float v = amount * (float)r / m;
                int whole = (int)v;
                amount = whole + (Sim.Roll(1000) < (v - whole) * 1000f ? 1 : 0);
            }
            return amount > 0 && Community.Striking(B[id].m_position) ? Community.StrikeIncome(amount) : amount;
        }

        static void RentPass(uint frame)
        {
            bool evict = Settings.Rent.value && (int)(frame - graceUntil) >= 0, grace = (int)(frame - graceUntil) < 0;
            float weeks = Sim.YearFrames / WeekFrames, share = Settings.SubsidyShare.value;
            CitizenUnit[] u = U;
            for (int k = 0, count = (int)((Buildings + Sim.YearFrames - 1) / Sim.YearFrames); k < count; k++)
            {
                ushort id = (ushort)rentCursor;
                if (++rentCursor >= Buildings)
                {
                    rentCursor = 0;
                    publicShare = allAcc > 0 ? (float)publicAcc / allAcc : 0f;
                    publicAcc = allAcc = 0;
                    homeBuildings = homeBuildingsAcc;
                    homeBuildingsAcc = 0;
                    Expire(frame);
                    for (int d = 0; d < Districts; d++)
                    {
                        districtPublic[d] = districtAllAcc[d] > 0 ? (float)districtPublicAcc[d] / districtAllAcc[d] : 0f;
                        districtPublicAcc[d] = districtAllAcc[d] = 0;
                    }
                    for (int d = 0; d <= Districts; d++) { tallying[d].CopyTo(tallied[d]); tallying[d].Clear(); }
                    Array.Copy(histogram, finalHistogram, Buckets);
                    Array.Clear(histogram, 0, Buckets);
                    if (desirabilityCount >= 16) meanDesirability = Mathf.Max(0.05f, (float)(desirabilitySum / desirabilityCount));
                    desirabilitySum = 0;
                    desirabilityCount = 0;
                    Community.Cycle();
                }
                if (!IsHome(id))
                {
                    rent[id] = market[id] = 0;
                    pressure[id] = burden[id] = discount[id] = 0;
                    flags[id] &= unchecked((byte)~(SeenFlag | GrandFlag));
                    continue;
                }
                byte policy = PolicyAt(B[id].m_position);
                bool forced = anyPublic && Forced(id), pub = forced || MarkedPublic(id), subsidy = (policy & SubsidyBit) != 0, controlled = (policy & ControlBit) != 0, low = IsLow(id);
                unitTmp.Clear(); incomeTmp.Clear(); tierTmp.Clear();
                int lowest = int.MaxValue, vacant = 0;
                double tenure = 0;
                uint unit = B[id].m_citizenUnits;
                for (int guard = 0; unit != 0 && guard < 1024; guard++)
                {
                    if ((u[unit].m_flags & CitizenUnit.Flags.Home) != 0)
                    {
                        if (u[unit].Empty()) { vacant++; owner[unit] = 0; }
                        else if (!Tracked(u[unit].m_citizen0))
                        {
                            int income = UnitIncome(unit), t = UnitTier(unit);
                            unitTmp.Add(unit); incomeTmp.Add(income); tierTmp.Add(t);
                            if (!Exempt(t)) lowest = Math.Min(lowest, income);
                            if (owner[unit] != u[unit].m_citizen0) { owner[unit] = u[unit].m_citizen0; movedIn[unit] = frame; }
                            tenure += frame - movedIn[unit];
                        }
                    }
                    unit = u[unit].m_nextUnit;
                }
                int homes = vacant + unitTmp.Count;
                float stay = pub ? 0f : ShortStay(id);
                int held = Math.Min(vacant, Mathf.RoundToInt(homes * stay)), listed = vacant - held;
                if (!pub && homes > 0 && (flags[id] & SeenFlag) != 0)
                {
                    float empty = (float)listed / homes;
                    if (empty >= VacantHigh) discount[id] = (byte)Math.Min(DiscountMax, discount[id] + DiscountStep);
                    else if (empty <= VacantLow) discount[id] = (byte)Math.Max(0, discount[id] - DiscountRecover);
                }
                else if (pub) discount[id] = 0;
                if (!pub)
                {
                    homeBuildingsAcc++;
                    Market(homes - held, listed);
                }
                int m = MarketRent(id, out float desirability), r = rent[id], prev = r;
                bool seen = (flags[id] & SeenFlag) != 0;
                desirabilitySum += desirability;
                desirabilityCount++;
                living[id] = (sbyte)Mathf.RoundToInt(100f * GoodsShare * (Mathf.Clamp(1f + PriceSlope * (desirability / Mathf.Max(0.05f, meanDesirability) - 1f), PriceLow, PriceHigh) - 1f));
                if ((flags[id] & SeenFlag) == 0)
                {
                    flags[id] |= SeenFlag;
                    r = m;
                    if (grace && !pub && lowest != int.MaxValue)
                    {
                        int cap = Mathf.FloorToInt(lowest * StayShare * Grandfather / (subsidy ? 1f - share : 1f));
                        if (cap < m) { r = Math.Max(1, cap); flags[id] |= GrandFlag; }
                    }
                }
                else if (r > 0 && m > r && (controlled || (flags[id] & GrandFlag) != 0))
                {
                    float keep = controlled ? Settings.ControlStrength.value : 0f;
                    if ((flags[id] & GrandFlag) != 0) keep = Math.Max(keep, 1f - CatchUp);
                    r += Mathf.CeilToInt((m - r) * (1f - keep));
                }
                else r = m;
                if (seen && prev > 0 && r > prev && unitTmp.Count > 0) r = Math.Min(r, prev + Math.Max(1, Mathf.CeilToInt(prev * LeaseStep)));
                if (r >= m) flags[id] &= unchecked((byte)~GrandFlag);
                market[id] = (ushort)m;
                rent[id] = (ushort)Mathf.Clamp(r, 1, ushort.MaxValue);
                float tenant = TenantRent(id, policy), cost = 0f, shares = 0f;
                int counted = 0, noticedUnits = 0, level = Mathf.Clamp(B[id].m_level, 0, 4);
                bool noticed = false;
                for (int j = 0; j < unitTmp.Count; j++)
                {
                    uint home = unitTmp[j];
                    int income = incomeTmp[j];
                    if (pub)
                    {
                        if (income * StayShare < m && !(low && forced && Owner(home))) cost += Utility[level] * RentCents;
                        notices.Remove(home);
                        continue;
                    }
                    if (low && Owner(home))
                    {
                        if (evict && Destitute(home))
                        {
                            noticed = true;
                            noticedUnits++;
                            if (notices.Count < Notices && !notices.ContainsKey(home)) notices[home] = frame + Sim.YearFrames / 4;
                        }
                        else notices.Remove(home);
                        continue;
                    }
                    if (Exempt(tierTmp[j])) { notices.Remove(home); continue; }
                    if (subsidy) cost += rent[id] * share * RentCents;
                    shares += income > 0 ? Mathf.Min(2f, tenant / income) : 2f;
                    counted++;
                    Pool(home, income);
                    if (evict && income * StayShare < tenant)
                    {
                        noticed = true;
                        noticedUnits++;
                        if (notices.Count < Notices && !notices.ContainsKey(home)) notices[home] = frame + Sim.YearFrames / 4;
                        Shop(home, tenant, frame);
                    }
                    else
                    {
                        notices.Remove(home);
                        float squeeze = (income > 0 ? tenant / income : 2f) + living[id] / 100f + Heating(id);
                        if (evict && squeeze > SpendStart) Shop(home, tenant, frame);
                        else seeking.Remove(home);
                    }
                }
                burden[id] = noticed ? NoticeBurden : (byte)Mathf.Clamp(counted > 0 ? Mathf.RoundToInt(shares / counted * 100f) : 0, 0, 200);
                allAcc += unitTmp.Count;
                if (pub) publicAcc += unitTmp.Count;
                byte district = Singleton<DistrictManager>.instance.GetDistrict(B[id].m_position);
                districtAllAcc[district] += unitTmp.Count;
                if (pub) districtPublicAcc[district] += unitTmp.Count;
                Community.Sample(B[id].m_position, unitTmp.Count, shares, counted, noticedUnits, tenure);
                Accumulate(tallying[district], unitTmp.Count, vacant, pub ? 0f : tenant, shares, counted, noticedUnits, pub);
                Accumulate(tallying[CityTally], unitTmp.Count, vacant, pub ? 0f : tenant, shares, counted, noticedUnits, pub);
                if (cost > 0f) Singleton<EconomyManager>.instance.FetchResource(EconomyManager.Resource.PolicyCost, Mathf.RoundToInt(cost * weeks), B[id].Info.m_class);
                if (held > 0)
                {
                    Singleton<EconomyManager>.instance.AddResource(EconomyManager.Resource.TourismIncome, Mathf.RoundToInt(held * rent[id] * RentCents * StrTax * weeks), B[id].Info.m_class);
                    if (held >= 3 && Sim.Roll(200u) == 0) Chirps.Send("ShortStay", 0);
                }
            }
        }

        static void Accumulate(Tally t, int homes, int vacant, float tenant, float shares, int counted, int notices, bool pub)
        {
            t.Households += homes;
            t.Vacant += vacant;
            t.Notices += notices;
            t.Counted += counted;
            t.Shares += shares;
            if (pub) t.Public += homes;
            else t.Rent += tenant * homes;
            for (int j = 0; j < incomeTmp.Count; j++)
            {
                t.Income += incomeTmp[j];
                if (tierTmp[j] == Rich) t.Rich++;
                else if (tierTmp[j] == Super) t.Super++;
                if (t == tallying[CityTally]) histogram[Math.Min(Buckets - 1, incomeTmp[j] / BucketSize)]++;
            }
        }

        internal static int RentRaw(ushort id) => rent[id];
        internal static int RentNow(ushort id) => rent[id] != 0 ? rent[id] : MarketRent(id);
        internal static int MarketNow(ushort id) => market[id] != 0 ? market[id] : RentNow(id);
        internal static int DiscountNow(ushort id) => discount[id];

        internal static float DesirabilityNow(ushort id)
        {
            MarketRent(id, out float d);
            return d;
        }

        internal static float TenantNow(ushort id)
        {
            if (IsPublic(id)) return 0f;
            float r = RentNow(id);
            return (PolicyAt(B[id].m_position) & SubsidyBit) != 0 ? r * (1f - Settings.SubsidyShare.value) : r;
        }

        internal static void Households(ushort id, List<HouseholdView> into)
        {
            into.Clear();
            CitizenUnit[] u = U;
            float tenant = TenantNow(id);
            uint unit = B[id].m_citizenUnits;
            for (int guard = 0; unit != 0 && guard < 1024; guard++)
            {
                if ((u[unit].m_flags & CitizenUnit.Flags.Home) != 0 && !u[unit].Empty())
                {
                    int people = 0;
                    for (int i = 0; i < 5; i++) if (u[unit].GetCitizen(i) != 0) people++;
                    int income = UnitIncome(unit);
                    bool owns = IsLow(id) && Owner(unit);
                    into.Add(new HouseholdView { People = people, Income = income, Tier = UnitTier(unit), Share = income > 0 && !owns ? tenant / income : 0f, Notice = notices.ContainsKey(unit), Owner = owns });
                }
                unit = u[unit].m_nextUnit;
            }
        }

        static void NoticePass(uint frame)
        {
            due.Clear();
            foreach (var n in notices) if ((int)(frame - n.Value) >= 0) due.Add(n.Key);
            CitizenUnit[] u = U;
            foreach (uint unit in due)
            {
                notices.Remove(unit);
                ushort id = u[unit].m_building;
                if (!Settings.Rent.value || (u[unit].m_flags & CitizenUnit.Flags.Home) == 0 || u[unit].Empty() || !IsHome(id) || IsPublic(id) || Tracked(u[unit].m_citizen0) || Exempt(UnitTier(unit))) continue;
                if (UnitIncome(unit) * StayShare < TenantRent(id, PolicyAt(B[id].m_position))) Evict(id, unit, frame);
            }
        }

        static void Pool(uint unit, int income)
        {
            if (poolCount < PoolSize)
            {
                poolUnit[poolCount] = unit;
                poolIncome[poolCount++] = income;
                return;
            }
            int worst = 0;
            for (int i = 1; i < PoolSize; i++) if (poolIncome[i] > poolIncome[worst]) worst = i;
            if (income >= poolIncome[worst]) return;
            poolUnit[worst] = unit;
            poolIncome[worst] = income;
        }

        static int Collect(uint unit)
        {
            int n = 0;
            Citizen[] z = C;
            for (int i = 0; i < 5; i++)
            {
                uint c = U[unit].GetCitizen(i);
                if (c != 0 && (z[c].m_flags & Citizen.Flags.Created) != 0 && !z[c].Dead && !byCitizen.ContainsKey(c)) scratch[n++] = c;
            }
            return n;
        }

        static void Evict(ushort building, uint unit, uint frame)
        {
            int n = Collect(unit);
            if (n == 0) return;
            Citizen[] z = C;
            for (int i = 0; i < n; i++) z[scratch[i]].SetHome(scratch[i], 0, 0u);
            Chirps.Send("Evicted", scratch[0]);
            Community.Displaced(B[building].m_position, 1);
            if (Settings.Homeless.value) Register(n, Sidewalk(building), frame);
            else for (int i = 0; i < n; i++) Singleton<CitizenManager>.instance.ReleaseCitizen(scratch[i]);
        }

        static Vector3 Sidewalk(ushort id) => Building.CalculateSidewalkPosition(B[id].m_position, B[id].m_angle, B[id].Length);

        static void Register(int n, Vector3 origin, uint frame)
        {
            Citizen[] z = C;
            var h = new Household { Since = frame, Next = frame + 64, Charged = frame, Origin = origin };
            int income = 0;
            for (int i = 0; i < n; i++)
            {
                uint c = scratch[i];
                if (byCitizen.ContainsKey(c)) continue;
                h.Members[h.Count++] = c;
                byCitizen[c] = h;
                income += CitizenIncome(c);
                h.Car |= z[c].m_parkedVehicle != 0 || z[c].WealthLevel == Citizen.Wealth.High || Exempt(tier[c]);
            }
            if (h.Count == 0) return;
            h.Car |= income >= CarIncome;
            households.Add(h);
        }

        static void VacancyPass()
        {
            for (int k = 0; k < VacancyPerFrame; k++)
            {
                ushort id = (ushort)vacancyCursor;
                vacancyCursor = (vacancyCursor + 1) % Buildings;
                if (!IsHome(id) || IsShelter(id)) continue;
                uint unit = B[id].GetEmptyCitizenUnit(CitizenUnit.Flags.Home);
                if (unit == 0) continue;
                if (IsPublic(id))
                {
                    pubBuilding[pubNext] = id;
                    pubUnit[pubNext] = unit;
                    pubNext = (pubNext + 1) % PublicSlots;
                }
                else
                {
                    vacBuilding[vacNext] = id;
                    vacUnit[vacNext] = unit;
                    vacNext = (vacNext + 1) % VacancySlots;
                }
            }
        }

        static bool FreeUnit(ushort id, uint unit) => unit != 0 && IsHome(id) && U[unit].m_building == id && (U[unit].m_flags & CitizenUnit.Flags.Home) != 0 && U[unit].Empty();

        static void PublicPass()
        {
            int slot = -1;
            for (int i = 0; i < PublicSlots && slot < 0; i++)
            {
                if (pubUnit[i] == 0) continue;
                if (FreeUnit(pubBuilding[i], pubUnit[i]) && IsPublic(pubBuilding[i])) slot = i;
                else pubUnit[i] = 0;
            }
            if (slot < 0) return;
            ushort target = pubBuilding[slot];
            uint targetUnit = pubUnit[slot];
            Citizen[] z = C;
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < households.Count; i++)
            {
                float d = VectorUtils.LengthSqrXZ(households[i].Origin - B[target].m_position);
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            if (best >= 0)
            {
                Household h = households[best];
                pubUnit[slot] = 0;
                for (int i = 0; i < h.Count; i++) z[h.Members[i]].SetHome(h.Members[i], 0, targetUnit);
                Chirps.Send("PublicHousing", h.Members[0]);
                RemoveAt(best);
                return;
            }
            int pick = -1;
            for (int i = 0; i < poolCount; i++)
            {
                uint unit = poolUnit[i];
                ushort home = U[unit].m_building;
                if ((U[unit].m_flags & CitizenUnit.Flags.Home) == 0 || U[unit].Empty() || home == 0 || IsPublic(home) || !IsHome(home) || Exempt(UnitTier(unit))) { poolIncome[i] = int.MaxValue; continue; }
                if (pick < 0 || poolIncome[i] < poolIncome[pick]) pick = i;
            }
            if (pick < 0 || poolIncome[pick] == int.MaxValue) return;
            int n = Collect(poolUnit[pick]);
            poolUnit[pick] = poolUnit[--poolCount];
            poolIncome[pick] = poolIncome[poolCount];
            if (n == 0) return;
            pubUnit[slot] = 0;
            for (int i = 0; i < n; i++) z[scratch[i]].SetHome(scratch[i], 0, targetUnit);
            Chirps.Send("PublicHousing", scratch[0]);
        }

        static void RebuildPresence()
        {
            Array.Clear(presence, 0, presence.Length);
            Array.Clear(streets, 0, streets.Length);
            Array.Clear(camp, 0, camp.Length);
            int street = 0, car = 0, sheltered = 0, staying = 0, jailed = 0;
            foreach (Household h in households)
            {
                if (h.Car) car++;
                else street++;
                bool shelter = h.Shelter != 0 && IsShelter(h.Shelter);
                if (shelter) sheltered++;
                if (h.Staying) staying++;
                if (Jailed(h))
                {
                    jailed++;
                    Strike(h);
                    continue;
                }
                if (h.Struck && h.Tent == 0)
                {
                    h.Struck = false;
                    if (Settings.Tents.value && !h.Car && h.Shelter != 0 && !shelter) Pitch(h, h.Shelter);
                }
                int c = Walk.Cell(h.Shelter != 0 ? B[h.Shelter].m_position : h.Origin);
                if (c < 0 || h.Car) continue;
                presence[c] += 1f;
                if (shelter) continue;
                streets[c] += h.Tent != 0 ? 1.5f : 1f;
                camp[c] += h.Tent != 0 ? 1f : DoorLone;
            }
            foreach (Household h in households)
            {
                if (h.Car || h.Shelter == 0 || IsShelter(h.Shelter) || h.Tent == 0 && h.Struck) continue;
                int c = Walk.Cell(B[h.Shelter].m_position);
                if (c < 0 || camps.Contains(c) || streets[c] < CampSize) continue;
                camps.Add(c);
                Chirps.Send("Encampment", 0);
            }
            if (camps.Count > 0) camps.RemoveWhere(c => streets[c] < CampGone);
            StreetHomeless = street;
            CarHomeless = car;
            Sheltered = sheltered;
            StayingHomeless = staying;
            JailedHomeless = jailed;
        }

        static void HouseholdPass(uint frame)
        {
            for (int k = 0, budget = Math.Max(1, households.Count / 64); k < budget && households.Count > 0; k++)
            {
                if (householdCursor >= households.Count) householdCursor = 0;
                Household h = households[householdCursor];
                if ((int)(frame - h.Next) < 0 || Process(h, frame)) householdCursor++;
                else RemoveAt(householdCursor);
            }
        }

        static void Drop(Household h, int i)
        {
            if (byCitizen.TryGetValue(h.Members[i], out Household o) && o == h) byCitizen.Remove(h.Members[i]);
            h.Members[i] = h.Members[--h.Count];
        }

        internal static void Left(uint c)
        {
            if (!byCitizen.TryGetValue(c, out Household h)) return;
            for (int i = 0; i < h.Count; i++)
                if (h.Members[i] == c) { Drop(h, i); return; }
            byCitizen.Remove(c);
        }

        static void ChargeShelter(Household h, uint frame)
        {
            uint elapsed = frame - h.Charged;
            if (h.Shelter == 0 || !IsShelter(h.Shelter) || (B[h.Shelter].m_flags & (Building.Flags.Abandoned | Building.Flags.Collapsed)) != 0) { h.Charged = frame; return; }
            if (elapsed < StayInterval) return;
            h.Charged = frame;
            BuildingInfo info = B[h.Shelter].Info;
            if (info == null) return;
            float cost = ShelterCents * elapsed / WeekFrames;
            if (info.m_buildingAI is WorshipAI && Religion.On && Religion.OutreachAt(B[h.Shelter].m_position)) cost *= 0.5f;
            Singleton<EconomyManager>.instance.FetchResource(EconomyManager.Resource.PolicyCost, Mathf.RoundToInt(cost), info.m_class);
        }

        static bool Process(Household h, uint frame)
        {
            Citizen[] z = C;
            int income = 0, t = Unrolled;
            bool wealthy = false, lost = h.Shelter == 0 || (B[h.Shelter].m_flags & (Building.Flags.Created | Building.Flags.Deleted)) != Building.Flags.Created;
            for (int i = 0; i < h.Count;)
            {
                uint c = h.Members[i];
                ushort home = z[c].m_homeBuilding;
                if ((z[c].m_flags & Citizen.Flags.Created) == 0 || z[c].Dead || (home != 0 && home != h.Shelter)) { Drop(h, i); continue; }
                lost |= home == 0;
                income += CitizenIncome(c);
                t = Math.Max(t, tier[c]);
                wealthy |= z[c].WealthLevel == Citizen.Wealth.High || Exempt(tier[c]);
                i++;
            }
            if (h.Count == 0) return false;
            if (Rehouse(h, income, t))
            {
                Chirps.Send("Rehoused", h.Members[0]);
                return false;
            }
            if (lost) Shelter(h);
            ChargeShelter(h, frame);
            if (!h.Staying)
            {
                if (frame - h.Since < SearchFrames)
                {
                    h.Next = frame + SearchInterval;
                    return true;
                }
                int chance = wealthy || income >= WealthyIncome ? 0 : income < PoorIncome ? StayPoor : StayMiddle;
                if (Sim.Roll(100) >= chance)
                {
                    Chirps.Send("LeftCity", h.Members[0]);
                    for (int i = 0; i < h.Count; i++) byCitizen.Remove(h.Members[i]);
                    for (int i = 0; i < h.Count; i++) Singleton<CitizenManager>.instance.ReleaseCitizen(h.Members[i]);
                    h.Count = 0;
                    return false;
                }
                h.Staying = true;
                Chirps.Send("Settled", h.Members[0]);
            }
            h.Next = frame + StayInterval;
            return true;
        }

        static void RemoveAt(int index)
        {
            Household h = households[index];
            for (int i = 0; i < h.Count; i++)
                if (byCitizen.TryGetValue(h.Members[i], out Household o) && o == h) byCitizen.Remove(h.Members[i]);
            Leave(h);
            int last = households.Count - 1;
            households[index] = households[last];
            households.RemoveAt(last);
        }

        static void Leave(Household h)
        {
            if (h.Shelter != 0)
            {
                occupancy[h.Shelter] = Math.Max(0, Occupied(h.Shelter) - 1);
                if (h.Tent != 0) Count(tentsAt, h.Shelter, -1);
                if (h.Slot < TentSlots)
                {
                    byte m = (byte)(Mask(h.Shelter) & ~(1 << h.Slot));
                    if (m == 0) slotMask.Remove(h.Shelter);
                    else slotMask[h.Shelter] = m;
                }
            }
            Tents.Remove(h.Tent);
            h.Tent = 0;
            h.Slot = Household.NoSlot;
        }

        static bool Rehouse(Household h, int income, int t)
        {
            if (Settings.Rent.value)
            {
                int slot = Vacancy(income, t, h.Origin, float.MaxValue, 0);
                if (slot < 0) return false;
                for (int i = 0; i < h.Count; i++) scratch[i] = h.Members[i];
                MoveInto(slot, h.Count);
                return true;
            }
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < VacancySlots; i++)
            {
                ushort id = vacBuilding[i];
                if (vacUnit[i] == 0) continue;
                if (!FreeUnit(id, vacUnit[i]) || IsPublic(id)) { vacUnit[i] = 0; continue; }
                float d = VectorUtils.LengthSqrXZ(B[id].m_position - h.Origin);
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            if (best < 0) return false;
            uint target = vacUnit[best];
            vacUnit[best] = 0;
            Citizen[] z = C;
            for (int i = 0; i < h.Count; i++) z[h.Members[i]].SetHome(h.Members[i], 0, target);
            return true;
        }

        static int Occupied(ushort id) => occupancy.TryGetValue(id, out int n) ? n : 0;

        internal static bool IsPark(ushort id)
        {
            BuildingInfo info = B[id].Info;
            BuildingAI ai = info != null ? info.m_buildingAI : null;
            return ai is ParkAI || ai is ParkBuildingAI;
        }

        static int Capacity(ushort id)
        {
            Building.Flags f = B[id].m_flags;
            if ((f & (Building.Flags.Created | Building.Flags.Deleted)) != Building.Flags.Created) return 0;
            int area = B[id].Width * B[id].Length;
            if ((f & Building.Flags.Collapsed) != 0) return 0;
            if (IsShelter(id)) return (f & Building.Flags.Abandoned) != 0 ? Math.Max(1, area / 3) : Math.Max(4, area / 2);
            if ((f & Building.Flags.Abandoned) != 0) return Math.Max(1, area / 3);
            if (IsPark(id) && (f & Building.Flags.Completed) != 0) return Math.Max(2, area / 4);
            return 0;
        }

        static bool Anchor(ushort id, bool car)
        {
            if ((B[id].m_flags & (Building.Flags.Created | Building.Flags.Completed | Building.Flags.Deleted | Building.Flags.Abandoned | Building.Flags.Collapsed)) != (Building.Flags.Created | Building.Flags.Completed)) return false;
            BuildingInfo info = B[id].Info;
            return info != null && info.m_class.m_service != ItemClass.Service.Residential && !IsPark(id) && Occupied(id) < AnchorCapacity && (!car || Drivable(id));
        }

        static bool Drivable(ushort id)
        {
            ushort seg = B[id].m_accessSegment;
            if (seg == 0) return false;
            NetInfo n = Singleton<NetManager>.instance.m_segments.m_buffer[seg].Info;
            return n != null && (n.m_vehicleTypes & VehicleInfo.VehicleType.Car) != 0 && !n.IsPedestrianZoneRoad() && !n.IsPublicTransportRoad();
        }

        static Vector3 CarSpot(Household h)
        {
            Citizen[] z = C;
            VehicleParked[] parked = Singleton<VehicleManager>.instance.m_parkedVehicles.m_buffer;
            for (int i = 0; i < h.Count; i++)
            {
                ushort v = z[h.Members[i]].m_parkedVehicle;
                if (v != 0 && (parked[v].m_flags & (ushort)VehicleParked.Flags.Created) != 0) return parked[v].m_position;
            }
            return h.Origin;
        }

        internal static bool NearHome(ref CitizenInstance data)
        {
            ushort target = data.m_targetBuilding;
            if (target == 0 || (data.m_flags & CitizenInstance.Flags.TargetIsNode) != 0 || target != C[data.m_citizen].m_homeBuilding) return false;
            return VectorUtils.LengthSqrXZ(data.GetLastFramePosition() - B[target].m_position) <= WalkHome * WalkHome;
        }

        static ushort Search(Vector3 origin, bool anchor, bool car = false)
        {
            ushort[] grid = Singleton<BuildingManager>.instance.m_buildingGrid;
            Building[] b = B;
            int cx = (int)(origin.x / 64f + 135f), cz = (int)(origin.z / 64f + 135f);
            ushort best = 0;
            float bestScore = float.MaxValue;
            for (int z = Math.Max(0, cz - ShelterCells); z <= Math.Min(269, cz + ShelterCells); z++)
                for (int x = Math.Max(0, cx - ShelterCells); x <= Math.Min(269, cx + ShelterCells); x++)
                {
                    ushort id = grid[z * 270 + x];
                    for (int guard = 0; id != 0 && guard < 49152; guard++)
                    {
                        float score = float.MaxValue;
                        if (anchor) { if (Anchor(id, car)) score = car ? 1f : 1f / (1f + Gather * Mathf.Min(GatherCap, Walk.Sum(streets, b[id].m_position, nx, nz, nw))); }
                        else if (Capacity(id) > Occupied(id)) score = IsShelter(id) ? 0.5f : 1f;
                        if (score < float.MaxValue)
                        {
                            score *= VectorUtils.LengthSqrXZ(b[id].m_position - origin);
                            if (score < bestScore) { bestScore = score; best = id; }
                        }
                        id = b[id].m_nextGridBuilding;
                    }
                }
            return best;
        }

        static int Slots(ushort id) => IsPark(id) ? TentSlots : Mathf.Clamp((int)(B[id].Width * 8f / Tents.Spacing), 1, TentSlots);

        static void Pitch(Household h, ushort target)
        {
            byte m = Mask(target);
            for (int i = 0, n = Slots(target); i < n && Tents.CanPlace; i++)
            {
                if ((m & (1 << i)) != 0) continue;
                ushort t = PlaceTent(target, i);
                if (t == 0) continue;
                h.Tent = t;
                h.Slot = (byte)i;
                slotMask[target] = (byte)(m | (1 << i));
                Count(tentsAt, target, 1);
                return;
            }
        }

        static void Shelter(Household h)
        {
            Leave(h);
            ushort target;
            bool street = true;
            if (h.Car)
            {
                Vector3 spot = CarSpot(h);
                target = Search(spot, true, true);
                if (target == 0) target = Search(spot, true);
            }
            else
            {
                target = Search(h.Origin, false);
                street = target == 0;
                if (street) target = Search(h.Origin, true);
            }
            h.Shelter = target;
            if (target == 0) return;
            uint unit = AcquireUnit(target);
            if (unit == 0) { h.Shelter = 0; return; }
            occupancy[target] = Occupied(target) + 1;
            if (!h.Car && street) { int cell = Walk.Cell(B[target].m_position); if (cell >= 0) streets[cell] += 1f; }
            Citizen[] z = C;
            for (int i = 0; i < h.Count; i++) if (z[h.Members[i]].m_homeBuilding != target) z[h.Members[i]].SetHome(h.Members[i], 0, unit);
            if (!Settings.Tents.value || h.Car || !(street || IsPark(target))) return;
            Pitch(h, target);
        }

        static uint AcquireUnit(ushort id)
        {
            uint unit = B[id].GetEmptyCitizenUnit(CitizenUnit.Flags.Home);
            if (unit != 0) return unit;
            CitizenManager cm = Singleton<CitizenManager>.instance;
            if (!cm.CreateUnits(out unit, ref Singleton<SimulationManager>.instance.m_randomizer, id, 0, 1)) return 0;
            uint last = B[id].m_citizenUnits;
            if (last == 0) B[id].m_citizenUnits = unit;
            else
            {
                for (int guard = 0; cm.m_units.m_buffer[last].m_nextUnit != 0 && guard < 65536; guard++) last = cm.m_units.m_buffer[last].m_nextUnit;
                cm.m_units.m_buffer[last].m_nextUnit = unit;
            }
            if (!extra.TryGetValue(id, out List<uint> list)) extra[id] = list = new List<uint>();
            list.Add(unit);
            return unit;
        }

        static void ReleaseExtra(ushort id)
        {
            if (!extra.TryGetValue(id, out List<uint> list)) return;
            extra.Remove(id);
            CitizenManager cm = Singleton<CitizenManager>.instance;
            CitizenUnit[] u = cm.m_units.m_buffer;
            Citizen[] z = C;
            foreach (uint unit in list)
            {
                if (u[unit].m_building != id) continue;
                for (int i = 0; i < 5; i++)
                {
                    uint c = u[unit].GetCitizen(i);
                    if (c != 0 && z[c].m_homeBuilding == id) z[c].SetHome(c, 0, 0u);
                }
                uint prev = 0, cur = B[id].m_citizenUnits;
                for (int guard = 0; cur != 0 && guard < 65536; guard++)
                {
                    if (cur == unit)
                    {
                        uint next = u[cur].m_nextUnit;
                        if (prev == 0) B[id].m_citizenUnits = next;
                        else u[prev].m_nextUnit = next;
                        u[unit].m_nextUnit = 0;
                        cm.ReleaseUnits(unit);
                        break;
                    }
                    prev = cur;
                    cur = u[cur].m_nextUnit;
                }
            }
        }

        static ushort PlaceTent(ushort id, int slot)
        {
            Vector3 p;
            float angle = B[id].m_angle, spacing = Tents.Spacing;
            float c = Mathf.Cos(angle), sn = Mathf.Sin(angle);
            if (IsPark(id))
            {
                float sx = Mathf.Min(spacing + 1f, B[id].Width * 8f / 3.5f), sz = Mathf.Min(spacing + 1f, B[id].Length * 8f / 2.5f);
                float x = (slot % 3 - 1) * sx + Jitter(spacing * 0.12f), z = (slot / 3 - 0.5f) * sz + Jitter(spacing * 0.12f);
                p = B[id].m_position + new Vector3(c * x - sn * z, 0f, sn * x + c * z);
                angle = Sim.Roll(3600) * (Mathf.PI / 1800f);
            }
            else
            {
                int n = Slots(id);
                p = Building.CalculateSidewalkPosition(B[id].m_position, angle, B[id].Length, (slot - (n - 1) * 0.5f) * spacing + Jitter(spacing * 0.1f), 1.6f + Jitter(0.25f));
                angle = Sim.Roll(3600) * (Mathf.PI / 1800f);
            }
            p.y = Mathf.Max(Singleton<TerrainManager>.instance.SampleDetailHeight(p), B[id].m_position.y);
            return Tents.Place(p, angle);
        }

        static float Jitter(float range) => (Sim.Roll(2001) - 1000) * 0.001f * range;

        internal static bool Tracked(uint c) => c != 0 && byCitizen.Count != 0 && byCitizen.ContainsKey(c);

        internal static bool Car(uint c, out bool car)
        {
            car = byCitizen.TryGetValue(c, out Household h) && h.Car;
            return h != null;
        }

        internal static bool Keep(uint c)
        {
            if (InRemove || !byCitizen.ContainsKey(c)) return false;
            Citizen[] z = C;
            return !z[c].Dead && z[c].m_homeBuilding == 0 && z[c].m_instance == 0 && z[c].m_vehicle == 0;
        }

        internal static bool FreeTransit(Vector3 p)
        {
            DistrictManager dm = Singleton<DistrictManager>.instance;
            return ((dm.m_districts.m_buffer[dm.GetDistrict(p)].m_servicePolicies | dm.m_districts.m_buffer[0].m_servicePolicies) & DistrictPolicies.Services.FreeTransport) != 0;
        }

        internal static void Pension(uint c, ref Citizen data)
        {
            Citizen.AgeGroup g = Citizen.GetAgeGroup(data.Age);
            if (g <= Citizen.AgeGroup.Teen) { pension[c] = 0; return; }
            if (g != Citizen.AgeGroup.Senior)
            {
                if (data.m_workBuilding != 0 && (data.m_flags & Citizen.Flags.Student) == 0) pension[c] = (byte)Mathf.Clamp(Earned(ref data, data.m_workBuilding) / 5, 1, 127);
                return;
            }
            if ((pension[c] & 0x80) != 0) return;
            int wage = (pension[c] & 0x7F) * 5;
            if (wage == 0) wage = Wage[Mathf.Clamp((int)data.EducationLevel, 0, 3)];
            int roll = Sim.Roll(100);
            if (roll < 20) wage = wage * 4 / 5;
            else if (roll >= 85) wage = wage * 6 / 5;
            pension[c] = (byte)(0x80 | Mathf.Clamp(wage / 5, 1, 127));
        }

        internal static bool HomelessTick(uint c, ref Citizen data)
        {
            if (!byCitizen.TryGetValue(c, out Household h)) return false;
            data.m_wellbeing = (byte)Mathf.Min(data.m_wellbeing, h.Staying ? 40 : 70);
            ushort at = data.m_homeBuilding;
            Vector3 where = at != 0 ? B[at].m_position : h.Origin;
            data.m_health = (byte)Mathf.Max((h.Staying ? 15 : 30) + Community.AidFloor(where), data.m_health - (h.Staying ? 4 : 1));
            if (at != 0 && !data.Arrested && (B[at].m_flags & (Building.Flags.Abandoned | Building.Flags.Collapsed)) == 0)
            {
                int cap = Math.Min(ushort.MaxValue, (Occupied(at) + B[at].m_citizenCount) * CrimeCap);
                if (B[at].m_crimeBuffer < cap)
                {
                    int rate = Citizen.GetCrimeRate(Citizen.GetWellbeingLevel(data.EducationLevel, data.m_wellbeing), data.Unemployed, false) * (h.Car ? CarCrime : StreetCrime) / CrimeDivisor;
                    B[at].m_crimeBuffer = (ushort)Math.Min(cap, B[at].m_crimeBuffer + rate);
                }
                if (!IsShelter(at) && Criminalized(where) && Citizen.GetAgeGroup(data.Age) >= Citizen.AgeGroup.Young)
                {
                    data.Criminal = true;
                    if (data.CurrentLocation == Citizen.Location.Home && Sim.Roll(3) == 0) CallPolice(at);
                }
            }
            float cold = ColdRisk(), heat = HeatRisk();
            if (cold > 0f || heat > 0f)
            {
                ushort sh = h.Shelter;
                bool inside = sh != 0 && IsShelter(sh) && (B[sh].m_flags & (Building.Flags.Abandoned | Building.Flags.Collapsed)) == 0 || Indoors(ref data);
                if (!inside)
                {
                    bool squat = sh != 0 && (B[sh].m_flags & Building.Flags.Abandoned) != 0;
                    cold *= h.Car ? CarCold : squat ? SquatExposure : 1f;
                    heat *= h.Car ? CarHot : squat ? SquatExposure : 1f;
                    float risk = cold + heat;
                    if (Sim.Roll(1000u) < ExposureDeath * risk)
                    {
                        data.Dead = true;
                        Chirps.Send(cold >= heat ? "Frozen" : "Heatstroke", 0);
                        return true;
                    }
                    if (!data.Sick && Sim.Roll(1000u) < ExposureSick * Mathf.Sqrt(Mathf.Min(1f, risk))) data.Sick = true;
                }
            }
            if (!h.Staying) return true;
            if (data.m_workBuilding != 0 && (data.m_flags & Citizen.Flags.Student) == 0 && Sim.Roll(1000) < JobLoss) data.SetWorkplace(c, 0, 0u);
            int death = Citizen.GetAgeGroup(data.Age) == Citizen.AgeGroup.Senior ? 30 : 8;
            if (data.m_health < 25) death *= 2;
            death = Community.Aid(where, death);
            if (Sim.Roll(1000) < death)
            {
                data.Dead = true;
                Chirps.Send("Died", 0);
            }
            return true;
        }

        static bool Indoors(ref Citizen data)
        {
            if (data.m_vehicle != 0 || data.Collapsed) return false;
            ushort at;
            switch (data.CurrentLocation)
            {
                case Citizen.Location.Work: at = data.m_workBuilding; break;
                case Citizen.Location.Visit: at = data.m_visitBuilding; break;
                default: return false;
            }
            return at != 0 && (B[at].m_flags & (Building.Flags.Created | Building.Flags.Abandoned | Building.Flags.Collapsed)) == Building.Flags.Created;
        }

        static void CallPolice(ushort id)
        {
            if (!Singleton<UnlockManager>.instance.Unlocked(ItemClass.Service.PoliceDepartment)) return;
            Vehicle[] v = Singleton<VehicleManager>.instance.m_vehicles.m_buffer;
            ushort guest = B[id].m_guestVehicles;
            for (int guard = 0; guest != 0 && guard < 16384; guard++)
            {
                if (v[guest].m_transferType == (byte)TransferManager.TransferReason.Crime) return;
                guest = v[guest].m_nextGuestVehicle;
            }
            Singleton<TransferManager>.instance.AddOutgoingOffer(TransferManager.TransferReason.Crime, new TransferManager.TransferOffer { Priority = 3, Building = id, Position = B[id].m_position, Amount = 1 });
        }

        static bool Jailed(Household h)
        {
            Citizen[] z = C;
            bool any = false;
            for (int i = 0; i < h.Count; i++)
            {
                uint c = h.Members[i];
                if (c == 0 || z[c].Dead) continue;
                if (!z[c].Arrested) return false;
                any = true;
            }
            return any;
        }

        static void Strike(Household h)
        {
            if (h.Tent == 0) return;
            if (h.Shelter != 0)
            {
                Count(tentsAt, h.Shelter, -1);
                if (h.Slot < TentSlots)
                {
                    byte m = (byte)(Mask(h.Shelter) & ~(1 << h.Slot));
                    if (m == 0) slotMask.Remove(h.Shelter);
                    else slotMask[h.Shelter] = m;
                }
            }
            Tents.Remove(h.Tent);
            h.Tent = 0;
            h.Slot = Household.NoSlot;
            h.Struck = true;
        }

        internal static void Neighbourhood(uint citizenID, ref Citizen data)
        {
            ushort home = data.m_homeBuilding;
            Vector3 p = B[home].m_position;
            float delta = 0f;
            bool owns = IsLow(home) && Owner(data.GetContainingUnit(citizenID, B[home].m_citizenUnits, CitizenUnit.Flags.Home));
            byte policy = IsPublic(home) || owns ? (byte)0 : PolicyAt(p);
            if (owns && landDrop[home] > 0) delta -= OwnerAnger * Anger(home);
            if ((policy & ControlBit) != 0)
            {
                delta += ControlBase;
                if (rent[home] > 0 && market[home] > rent[home]) delta += ControlGap * Mathf.Clamp01(((float)market[home] / rent[home] - 1f) / ControlFullGap);
            }
            if ((policy & SubsidyBit) != 0) delta += SubsidyBonus;
            if (data.m_workBuilding != 0 && TentsAt(data.m_workBuilding) > 0) delta -= TentWorker;
            delta += Community.Wellbeing(p, Sim.Frame);
            if (Settings.Compensation.value)
                for (int i = 0; i < sites.Count; i++)
                {
                    float d = VectorUtils.LengthXZ(sites[i].Pos - p);
                    if (d < SiteRadius) delta -= SitePenalty * (1f - d / SiteRadius);
                }
            if (households.Count > 0) delta -= Mathf.Min(PresenceMax, PresencePerHousehold * Walk.Sum(presence, p, px, pz, pw));
            if (delta != 0f) data.m_wellbeing = (byte)Mathf.Clamp(data.m_wellbeing + Mathf.RoundToInt(delta), 0, 100);
        }

        internal static void TentHappiness(ushort id, ref Building data)
        {
            int n = TentsAt(id);
            if (n > 0) data.m_happiness = (byte)Math.Max(0, data.m_happiness - (int)Mathf.Min(TentPenaltyMax, TentPenalty * n));
        }

        internal static float StreetsNear(Vector3 p) => households.Count > 0 ? Walk.Sum(streets, p, nx, nz, nw) : 0f;

        internal const int GridCells = Cells;
        internal const float MapFull = 6f;

        internal static void CopyStreets(float[] into)
        {
            lock (sync) Array.Copy(streets, into, Math.Min(streets.Length, into.Length));
        }

        internal static float StreetsIn(float[] grid, Vector3 p) => Walk.Sum(grid, p, nx, nz, nw);

        internal static int DoorstepNow(ushort id) => doorstep[id];
        internal static int AlarmNow(ushort id) => alarm[id];

        internal static void RentAlarm(ushort id, ref Building data)
        {
            int level = 0, old = alarm[id];
            if (notices.Count > 0 && (data.m_flags & (Building.Flags.Abandoned | Building.Flags.Collapsed)) == 0 && !IsPublic(id))
            {
                int homes = 0, noticed = 0;
                CitizenUnit[] u = U;
                uint unit = data.m_citizenUnits;
                for (int guard = 0; unit != 0 && guard < 1024; guard++)
                {
                    if ((u[unit].m_flags & CitizenUnit.Flags.Home) != 0 && !u[unit].Empty() && !Tracked(u[unit].m_citizen0))
                    {
                        homes++;
                        if (notices.ContainsKey(unit)) noticed++;
                    }
                    unit = u[unit].m_nextUnit;
                }
                if (noticed * 2 > homes) level = noticed * 4 >= homes * 3 ? 2 : 1;
            }
            if (level == old) return;
            alarm[id] = (byte)level;
            Badges.Set(Badges.Rent, id, ref data, old > 0, level > 0, old > 1, level > 1);
        }

        internal static void Doorstep(ushort id, ref Building data)
        {
            int old = doorstep[id];
            if (households.Count == 0 && old == 0) return;
            BuildingInfo info = data.Info;
            if (info == null) return;
            ItemClass.Service service = info.m_class.m_service;
            bool workplace = service == ItemClass.Service.Office || service == ItemClass.Service.Industrial;
            float s = 0f;
            if ((data.m_flags & (Building.Flags.Abandoned | Building.Flags.Collapsed)) == 0 && !MarkedPublic(id) && !IsShelter(id))
                s = Walk.Sum(camp, data.m_position, nx, nz, nw) + DoorTent * TentsAt(id);
            int level = Math.Max(0, old - 4);
            if (s >= DoorStart)
            {
                int cap = s >= DoorRed ? 255 : DoorMajor - 1;
                level = old > cap ? Math.Max(cap, old - 2) : Math.Min(cap, old + Mathf.Clamp((int)(s - DoorStart) + 1, 1, 3));
            }
            doorstep[id] = (byte)level;
            Badges.Update(id, ref data, old, level);
            if (!workplace && (data.m_problems & DoorIcon).IsNotNone) data.m_problems = Notification.RemoveProblems(data.m_problems, DoorIcon);
            if (level >= DoorMajor)
            {
                data.m_problems |= Notification.Problem1.MajorProblem;
                if (old < DoorMajor) Chirps.Send("DoorstepLeaving", 0);
                return;
            }
            if (old >= DoorMajor) data.m_problems = (data.m_problems & Notification.ProblemStruct.Mask).IsNone ? Notification.ProblemStruct.None : data.m_problems & Notification.ProblemStruct.Mask;
            if (level >= DoorMinor && old < DoorMinor) Chirps.Send("DoorstepComplaint", 0);
        }

        internal static int ParkOutput(ushort id, int rate)
        {
            int n = TentsAt(id);
            return n > 0 ? Mathf.RoundToInt(rate * Mathf.Max(ParkFloor, 1f - ParkLoss * n)) : rate;
        }

        internal static void TaxPressure(ushort id, ref Building data)
        {
            BuildingInfo info = data.Info;
            if (info == null || info.m_class.m_service != ItemClass.Service.Residential) return;
            Community.Watch(ref data);
            if (IsPublic(id)) return;
            int r = rent[id], m = market[id];
            int p = pressure[id];
            if (r > 0 && m > r && Controlled(id))
            {
                int eff = Singleton<EconomyManager>.instance.GetTaxRate(info.m_class) * m / r;
                int threshold = PressureThreshold + (int)Citizen.GetWealthLevel((ItemClass.Level)data.m_level) + (info.m_class.isResidentialHighGeneric ? 1 : 0);
                p = eff >= threshold ? Math.Min(255, p + Math.Max(2, (eff - threshold) / 4 + 1)) : Math.Max(0, p - 1);
            }
            else p = Math.Max(0, p - 2);
            pressure[id] = (byte)p;
            if (p >= 96) data.m_problems = Notification.AddProblems(data.m_problems, Notification.Problem1.TaxesTooHigh | Notification.Problem1.MajorProblem);
            else if (p >= 32) data.m_problems = Notification.AddProblems(data.m_problems, Notification.Problem1.TaxesTooHigh);
        }

        static double Value(int n, float level)
        {
            Citizen[] z = C;
            float weight = 0f, education = 0f;
            int adults = 0;
            for (int i = 0; i < n; i++)
            {
                Citizen.AgeGroup g = Citizen.GetAgeGroup(z[scratch[i]].Age);
                weight += g <= Citizen.AgeGroup.Teen ? CompChild : g == Citizen.AgeGroup.Senior ? CompSenior : CompAdult;
                if (g == Citizen.AgeGroup.Young || g == Citizen.AgeGroup.Adult) { education += (int)z[scratch[i]].EducationLevel; adults++; }
            }
            return weight * (1f + CompEducation * (adults > 0 ? education / adults : 0f)) * level;
        }

        static void Charge(ushort id, BuildingInfo info, double total, int count, uint sender)
        {
            if (sites.Count >= 64) sites.RemoveAt(0);
            sites.Add(new Site { Pos = B[id].m_position, Until = Sim.Frame + SiteFrames });
            int amount = (int)Math.Min(int.MaxValue, total * Community.Premium(B[id].m_position) * Settings.CompensationScale.value * 100.0);
            Singleton<EconomyManager>.instance.FetchResource(EconomyManager.Resource.LandPrice, amount, info.m_class);
            Chirps.Send("Compensation", sender, (amount / 100).ToString("N0"), count);
            Chirps.Send("Protest", 0);
        }

        internal static void Released(ushort id)
        {
            Badges.Forget(id);
            Building[] b = B;
            BuildingInfo info = b[id].Info;
            bool residential = info != null && info.m_class.m_service == ItemClass.Service.Residential;
            Building.Flags f = b[id].m_flags;
            bool home = residential && (f & (Building.Flags.Created | Building.Flags.Deleted | Building.Flags.Abandoned | Building.Flags.Collapsed)) == Building.Flags.Created;
            lock (sync)
            {
                flags[id] = 0;
                rent[id] = market[id] = 0;
                pressure[id] = burden[id] = alarm[id] = 0;
                extra.Remove(id);
                pending.Remove(id);
                if ((f & Building.Flags.Collapsed) != 0 && b[id].m_levelUpProgress != byte.MaxValue && (f & Building.Flags.Untouchable) == 0 && ToolsModifierControl.toolController != null && ToolsModifierControl.toolController.CurrentTool is BulldozeTool)
                {
                    int lost = 0;
                    for (uint cu = b[id].m_citizenUnits, hops = 0; cu != 0 && hops < 1024; cu = U[cu].m_nextUnit, hops++)
                        if ((U[cu].m_flags & CitizenUnit.Flags.Home) != 0) lost++;
                    Community.Displaced(b[id].m_position, Math.Max(1, lost));
                    Chirps.Send("Rubble", 0);
                }
                if (!home || (!Settings.Compensation.value && !Settings.Homeless.value)) return;
                CitizenUnit[] u = U;
                float level = 1f + CompLevel * Mathf.Clamp(b[id].m_level, 0, 4);
                double total = 0;
                int count = 0;
                uint sender = 0;
                uint unit = b[id].m_citizenUnits;
                for (int guard = 0; unit != 0 && guard < 1024; guard++)
                {
                    if ((u[unit].m_flags & CitizenUnit.Flags.Home) != 0)
                    {
                        int n = Collect(unit);
                        if (n > 0)
                        {
                            total += Value(n, level);
                            count++;
                            if (sender == 0) sender = scratch[0];
                            if (Settings.Homeless.value) Register(n, Sidewalk(id), Sim.Frame);
                        }
                    }
                    unit = u[unit].m_nextUnit;
                }
                if (count > 0 && Settings.Compensation.value) Charge(id, info, total, count, sender);
                Community.Displaced(b[id].m_position, count);
            }
        }

        internal static bool Removing(ushort id)
        {
            if (id == 0) return true;
            Building.Flags f = B[id].m_flags;
            bool collapsed = (f & Building.Flags.Collapsed) != 0;
            if (!IsResidential(id) || (!collapsed && (f & Building.Flags.Abandoned) == 0)) return collapsed;
            lock (sync)
            {
                CitizenUnit[] u = U;
                uint unit = B[id].m_citizenUnits;
                for (int guard = 0; unit != 0 && guard < 1024; guard++)
                {
                    if ((u[unit].m_flags & CitizenUnit.Flags.Home) != 0)
                    {
                        int n = Collect(unit);
                        if (n > 0 && Settings.Homeless.value) Register(n, Sidewalk(id), Sim.Frame);
                    }
                    unit = u[unit].m_nextUnit;
                }
            }
            return collapsed;
        }

        internal static byte[] Save()
        {
            lock (sync)
                using (var ms = new MemoryStream())
                using (var w = new BinaryWriter(ms))
                {
                    w.Write((byte)5);
                    w.Write(graceUntil);
                    w.Write(households.Count);
                    foreach (Household h in households)
                    {
                        w.Write((byte)((h.Staying ? 1 : 0) | (h.Car ? 2 : 0)));
                        w.Write((byte)h.Count);
                        for (int i = 0; i < h.Count; i++) w.Write(h.Members[i]);
                        w.Write(h.Shelter); w.Write(h.Tent); w.Write(h.Slot); w.Write(h.Since);
                        w.Write(h.Origin.x); w.Write(h.Origin.y); w.Write(h.Origin.z);
                    }
                    w.Write(sites.Count);
                    foreach (Site s in sites) { w.Write(s.Pos.x); w.Write(s.Pos.y); w.Write(s.Pos.z); w.Write(s.Until); }
                    w.Write(policies);
                    w.Write(notices.Count);
                    foreach (var n in notices) { w.Write(n.Key); w.Write(n.Value); }
                    w.Write(pending.Count);
                    foreach (var p in pending) { w.Write(p.Key); w.Write(p.Value.Amount); w.Write(p.Value.Count); w.Write(p.Value.Until); }
                    int count = 0;
                    for (int i = 0; i < Buildings; i++) if (flags[i] != 0 || rent[i] != 0) count++;
                    w.Write(count);
                    for (int i = 0; i < Buildings; i++)
                        if (flags[i] != 0 || rent[i] != 0) { w.Write((ushort)i); w.Write(flags[i]); w.Write(rent[i]); w.Write(market[i]); w.Write(pressure[i]); w.Write(burden[i]); w.Write(discount[i]); }
                    w.Write(extra.Count);
                    foreach (var e in extra) { w.Write(e.Key); w.Write(e.Value.Count); foreach (uint unit in e.Value) w.Write(unit); }
                    count = 0;
                    for (int i = 0; i < Citizens; i++) if (pension[i] != 0 || tier[i] != 0) count++;
                    w.Write(count);
                    for (int i = 0; i < Citizens; i++) if (pension[i] != 0 || tier[i] != 0) { w.Write((uint)i); w.Write(pension[i]); w.Write(tier[i]); }
                    count = 0;
                    for (int i = 0; i < Units; i++) if (owner[i] != 0) count++;
                    w.Write(count);
                    for (int i = 0; i < Units; i++) if (owner[i] != 0) { w.Write((uint)i); w.Write(owner[i]); w.Write(movedIn[i]); }
                    w.Write(meanDesirability);
                    Community.Save(w);
                    w.Write(marketFactor);
                    w.Write(emaHomes);
                    w.Write(emaVacant);
                    w.Write(homeBuildings);
                    int peaks = 0;
                    for (int i = 0; i < Buildings; i++) if (landPeak[i] > 0f) peaks++;
                    w.Write(peaks);
                    for (int i = 0; i < Buildings; i++) if (landPeak[i] > 0f) { w.Write((ushort)i); w.Write(landPeak[i]); }
                    w.Flush();
                    return ms.ToArray();
                }
        }

        internal static void Load(byte[] data)
        {
            Clear();
            if (data == null || data.Length == 0) return;
            lock (sync)
                using (var r = new BinaryReader(new MemoryStream(data)))
                {
                    if (r.ReadByte() != 5) return;
                    graceUntil = r.ReadUInt32();
                    for (int k = r.ReadInt32(); k > 0; k--)
                    {
                        byte state = r.ReadByte();
                        var h = new Household { Staying = (state & 1) != 0, Car = (state & 2) != 0 };
                        int n = r.ReadByte();
                        for (int i = 0; i < n; i++)
                        {
                            uint c = r.ReadUInt32();
                            if (h.Count < 5 && c != 0 && c < Citizens && !byCitizen.ContainsKey(c)) { h.Members[h.Count++] = c; byCitizen[c] = h; }
                        }
                        h.Shelter = r.ReadUInt16(); h.Tent = r.ReadUInt16(); h.Slot = r.ReadByte(); h.Since = r.ReadUInt32();
                        h.Origin = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        if (h.Count > 0) households.Add(h);
                    }
                    for (int k = r.ReadInt32(); k > 0; k--) sites.Add(new Site { Pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), Until = r.ReadUInt32() });
                    r.Read(policies, 0, policies.Length);
                    for (int k = r.ReadInt32(); k > 0; k--) notices[r.ReadUInt32()] = r.ReadUInt32();
                    for (int k = r.ReadInt32(); k > 0; k--) pending[r.ReadUInt16()] = new Pending { Amount = r.ReadDouble(), Count = r.ReadInt32(), Until = r.ReadUInt32() };
                    for (int k = r.ReadInt32(); k > 0; k--)
                    {
                        ushort i = r.ReadUInt16();
                        flags[i] = r.ReadByte(); rent[i] = r.ReadUInt16(); market[i] = r.ReadUInt16(); pressure[i] = r.ReadByte(); burden[i] = r.ReadByte(); discount[i] = r.ReadByte();
                    }
                    for (int k = r.ReadInt32(); k > 0; k--)
                    {
                        ushort id = r.ReadUInt16();
                        var list = new List<uint>();
                        for (int j = r.ReadInt32(); j > 0; j--) list.Add(r.ReadUInt32());
                        extra[id] = list;
                    }
                    for (int k = r.ReadInt32(); k > 0; k--)
                    {
                        uint c = r.ReadUInt32();
                        byte v = r.ReadByte(), t = r.ReadByte();
                        if (c < Citizens) { pension[c] = v; tier[c] = t; }
                    }
                    for (int k = r.ReadInt32(); k > 0; k--)
                    {
                        uint unit = r.ReadUInt32(), who = r.ReadUInt32(), since = r.ReadUInt32();
                        if (unit < Units) { owner[unit] = who; movedIn[unit] = since; }
                    }
                    meanDesirability = r.ReadSingle();
                    Community.Load(r);
                    if (r.BaseStream.Length - r.BaseStream.Position >= 16)
                    {
                        marketFactor = Mathf.Clamp(r.ReadSingle(), MarketLow, MarketHigh);
                        emaHomes = r.ReadSingle();
                        emaVacant = r.ReadSingle();
                        homeBuildings = r.ReadInt32();
                        if (r.BaseStream.Length - r.BaseStream.Position >= 4)
                            for (int k = r.ReadInt32(); k > 0; k--)
                            {
                                ushort i = r.ReadUInt16();
                                float v = r.ReadSingle();
                                if (i < Buildings) landPeak[i] = v;
                            }
                    }
                    loaded = true;
                }
        }
    }

    enum Need : byte { None, Transit, Services, Rent, Shelter, Quiet, Clean, NoDemolition }

    sealed class Association
    {
        public bool Active, Strike, Disturbed;
        public uint Forming, Deadline, NextDemand, Party, Met, Objected;
        public float Strength, Gripe, Displaced, Projects, Missed, Baseline, Burden, Notices, Tenure;
        public Need Demand;
        public int Calm, Homes;
        public Vector3 Center;
        public double SumBurden, SumTenure, SumX, SumZ;
        public int SumHomes, SumCounted, SumNotices;
    }

    static class Community
    {
        const int Districts = 128, FormPopulation = 500, PartyChance = 10;
        const uint Week = 4096, FormFrames = 16384, DemandFrames = 65536, MetFrames = 32768;
        const float StrengthBase = 15f, TenureWeight = 35f, GrievanceWeight = 50f, TenureYears = 3f, Approach = 0.1f, GripeApproach = 0.2f, SplitShare = 0.5f;
        const float RentStart = 0.35f, RentRange = 0.25f, StrikeBurden = 0.5f, StrikeNotices = 0.15f, CalmBurden = 0.42f, CalmNotices = 0.08f, StrikeStrength = 25f, StrikeTax = 0.5f;
        const float DisplacementDecay = 0.8f, DisplacementScale = 10f, DisplacementStrength = 2f, DisplacementCap = 15f, ProjectDecay = 0.85f, ProjectCap = 5f, MissedDecay = 0.9f;
        const float MetRelief = 20f, MissedAnger = 15f, ObjectionAnger = 6f, ObjectionShare = 0.5f, ObjectionCap = 0.5f, DemolitionPremium = 0.5f, Honeymoon = 0.5f;
        const float WatchShare = 0.02f, AidShare = 0.4f, AidHealth = 10f, PartyBonus = 6f, MetBonus = 5f, PartyGripe = 40f, MinNeed = 0.2f;

        static readonly Association[] all = new Association[Districts];
        static bool districts;
        static int government;

        static Community()
        {
            for (int i = 0; i < Districts; i++) all[i] = new Association();
        }

        internal static void Clear()
        {
            for (int i = 0; i < Districts; i++) all[i] = new Association();
            districts = false;
            government = 0;
        }

        static int Index(Vector3 p)
        {
            byte d = Singleton<DistrictManager>.instance.GetDistrict(p);
            return districts ? (d == 0 ? -1 : d) : 0;
        }

        static Association At(Vector3 p)
        {
            int i = Index(p);
            return i >= 0 && all[i].Active ? all[i] : null;
        }

        internal static float Strength(Vector3 p)
        {
            Association a = At(p);
            return a != null ? a.Strength / 100f : 0f;
        }

        internal static float Mobilization(Vector3 p)
        {
            Association a = At(p);
            return a != null ? a.Strength * a.Gripe / 10000f : 0f;
        }

        internal static bool Striking(Vector3 p)
        {
            Association a = At(p);
            return a != null && a.Strike;
        }

        internal static int StrikeIncome(int amount)
        {
            float v = amount * StrikeTax;
            int whole = (int)v;
            return whole + (Sim.Roll(1000) < (v - whole) * 1000f ? 1 : 0);
        }

        internal static void Sample(Vector3 p, int homes, float shares, int counted, int notices, double tenure)
        {
            int i = Index(p);
            if (i < 0 || homes == 0) return;
            Association a = all[i];
            a.SumHomes += homes;
            a.SumCounted += counted;
            a.SumNotices += notices;
            a.SumBurden += shares;
            a.SumTenure += tenure;
            a.SumX += p.x * homes;
            a.SumZ += p.z * homes;
        }

        internal static void Cycle()
        {
            foreach (Association a in all)
            {
                a.Homes = a.SumHomes;
                a.Burden = a.SumCounted > 0 ? (float)(a.SumBurden / a.SumCounted) : 0f;
                a.Notices = a.SumCounted > 0 ? (float)a.SumNotices / a.SumCounted : 0f;
                a.Tenure = a.SumHomes > 0 ? (float)(a.SumTenure / a.SumHomes) : 0f;
                if (a.SumHomes > 0) a.Center = new Vector3((float)(a.SumX / a.SumHomes), 0f, (float)(a.SumZ / a.SumHomes));
                a.SumBurden = a.SumTenure = a.SumX = a.SumZ = 0;
                a.SumHomes = a.SumCounted = a.SumNotices = 0;
            }
        }

        internal static void Displaced(Vector3 p, int households)
        {
            Association a = At(p);
            if (a == null || households <= 0) return;
            a.Displaced += households;
            a.Strength = Mathf.Min(100f, a.Strength + Mathf.Min(DisplacementCap, DisplacementStrength * households));
            a.Disturbed = true;
        }

        internal static double Premium(Vector3 p) => 1.0 + DemolitionPremium * Strength(p);

        internal static void Objection(Vector3 p, int cost, float weight, ItemClass cls)
        {
            Association a = At(p);
            if (a == null || cost <= 0 || weight <= 0f) return;
            int amount = Mathf.RoundToInt(cost * Mathf.Min(ObjectionCap, ObjectionShare * a.Strength / 100f * weight));
            if (Sim.Frame - a.Objected >= Week || a.Objected == 0)
            {
                a.Objected = Sim.Frame;
                a.Gripe = Mathf.Min(100f, a.Gripe + ObjectionAnger * weight);
                a.Projects = Mathf.Min(ProjectCap, a.Projects + weight);
            }
            if (amount <= 0) return;
            Singleton<EconomyManager>.instance.FetchResource(EconomyManager.Resource.Construction, amount, cls);
            Chirps.Send("Objection", 0, (amount / 100).ToString("N0"));
        }

        internal static float Wellbeing(Vector3 p, uint frame)
        {
            Association a = At(p);
            if (a == null) return 0f;
            float s = a.Strength / 100f, delta = 0f;
            if ((int)(frame - a.Party) < 0) delta += PartyBonus * s;
            if ((int)(frame - a.Met) < 0) delta += MetBonus;
            return delta;
        }

        internal static void Watch(ref Building data)
        {
            float s = Strength(data.m_position);
            if (s > 0f) data.m_crimeBuffer = (ushort)(data.m_crimeBuffer - (int)(data.m_crimeBuffer * WatchShare * s));
        }

        internal static int Aid(Vector3 p, int death) => Mathf.RoundToInt(death * (1f - AidShare * Strength(p)));
        internal static int AidFloor(Vector3 p) => Mathf.RoundToInt(AidHealth * Strength(p));

        static bool AnyDistricts()
        {
            District[] d = Singleton<DistrictManager>.instance.m_districts.m_buffer;
            for (int i = 1; i < Districts; i++) if ((d[i].m_flags & District.Flags.Created) != 0) return true;
            return false;
        }

        internal static void Step(uint frame)
        {
            if ((frame & (Week - 1)) != 0) return;
            bool now = AnyDistricts();
            if (now != districts)
            {
                float carried = now && all[0].Active ? all[0].Strength * SplitShare : 0f;
                for (int i = 0; i < Districts; i++)
                {
                    all[i] = new Association();
                    if (now && i > 0 && carried > 0f) all[i].Strength = carried;
                }
                districts = now;
            }
            int key = Politics.Government();
            if (key != government)
            {
                if (government != 0) foreach (Association a in all) a.Gripe *= Honeymoon;
                government = key;
            }
            District[] dm = Singleton<DistrictManager>.instance.m_districts.m_buffer;
            for (int i = districts ? 1 : 0; i < Districts; i++)
            {
                Association a = all[i];
                if (districts && (dm[i].m_flags & District.Flags.Created) == 0) { if (a.Active || a.Forming != 0) all[i] = new Association(); continue; }
                Update(a, dm[i].m_populationData.m_finalCount, frame);
            }
        }

        static void Update(Association a, uint population, uint frame)
        {
            if (!a.Active)
            {
                if (population < FormPopulation) { a.Forming = 0; return; }
                if (a.Forming == 0) { a.Forming = frame; return; }
                if (frame - a.Forming < FormFrames) return;
                a.Active = true;
                a.Strength = Mathf.Max(a.Strength, StrengthBase);
                a.NextDemand = frame + Week * 4;
                Chirps.Send("Organized", 0);
            }
            float rent = Mathf.Clamp01((a.Burden - RentStart) / RentRange + a.Notices);
            float grievance = Mathf.Clamp01(0.4f * rent + 0.3f * Mathf.Clamp01(a.Displaced / DisplacementScale) + 0.15f * Mathf.Clamp01(a.Missed / 3f) + 0.15f * Mathf.Clamp01(a.Projects / 3f));
            float tenure = Mathf.Clamp01(a.Tenure / (TenureYears * Sim.YearFrames));
            a.Strength += (Mathf.Min(100f, StrengthBase + TenureWeight * tenure + GrievanceWeight * grievance) - a.Strength) * Approach;
            a.Gripe = Mathf.Clamp(a.Gripe + (100f * grievance - a.Gripe) * GripeApproach, 0f, 100f);
            a.Displaced *= DisplacementDecay;
            a.Projects *= ProjectDecay;
            a.Missed *= MissedDecay;
            Strike(a);
            Demands(a, frame);
            if (a.Gripe < PartyGripe && (int)(frame - a.Party) >= 0 && Sim.Roll(100) < PartyChance)
            {
                a.Party = frame + Week;
                Chirps.Send("BlockParty", 0);
            }
            a.Disturbed = false;
        }

        static void Strike(Association a)
        {
            if (!Settings.Rent.value) { a.Strike = false; return; }
            if (!a.Strike)
            {
                if (a.Strength < StrikeStrength || (a.Burden < StrikeBurden && a.Notices < StrikeNotices)) return;
                a.Strike = true;
                a.Calm = 0;
                Chirps.Send("StrikeStart", 0);
                return;
            }
            a.Calm = a.Burden < CalmBurden && a.Notices < CalmNotices ? a.Calm + 1 : 0;
            if (a.Calm < 2) return;
            a.Strike = false;
            Chirps.Send("StrikeEnd", 0);
        }

        static float Measure(Need n, Association a)
        {
            Vector3 p = a.Center;
            switch (n)
            {
                case Need.Transit: return Walk.TransitAt(p);
                case Need.Services: return Walk.ServicesAt(p);
                case Need.Rent: return a.Burden;
                case Need.Shelter: return Housing.PresenceAt(p);
                case Need.Quiet:
                    Singleton<ImmaterialResourceManager>.instance.CheckLocalResource(ImmaterialResourceManager.Resource.NoisePollution, p, out int noise);
                    return noise;
                case Need.Clean:
                    Singleton<NaturalResourceManager>.instance.CheckPollution(p, out byte ground);
                    return ground;
                case Need.NoDemolition: return a.Displaced;
            }
            return 0f;
        }

        static float Urgency(Need n, float v)
        {
            switch (n)
            {
                case Need.Transit: return v < 0.3f ? 0.8f * (1f - v / 0.3f) : 0f;
                case Need.Services: return Mathf.Clamp01((0.5f - v) / 0.5f);
                case Need.Rent: return Settings.Rent.value ? Mathf.Clamp01((v - RentStart) / RentRange) : 0f;
                case Need.Shelter: return Settings.Homeless.value && v > 1f ? Mathf.Clamp01(v / 5f) : 0f;
                case Need.Quiet:
                case Need.Clean: return Mathf.Clamp01((v - 64f) / 128f);
                case Need.NoDemolition: return v > 2f ? Mathf.Clamp01(v / DisplacementScale) : 0f;
            }
            return 0f;
        }

        static bool Satisfied(Need n, float now, float start)
        {
            switch (n)
            {
                case Need.Transit: return now >= 0.4f || now >= start + 0.15f;
                case Need.Services: return now >= 0.6f || now >= start + 0.12f;
                case Need.Rent: return now <= 0.33f || now <= start - 0.1f;
                case Need.Shelter: return now <= 0.5f || now <= start * 0.5f;
                case Need.Quiet:
                case Need.Clean: return now <= 60f || now <= start * 0.8f;
            }
            return false;
        }

        static void Demands(Association a, uint frame)
        {
            if (a.Homes == 0) return;
            if (a.Demand == Need.None)
            {
                if ((int)(frame - a.NextDemand) < 0) return;
                Need best = Need.None;
                float score = MinNeed, value = 0f;
                for (Need n = Need.Transit; n <= Need.NoDemolition; n++)
                {
                    float v = Measure(n, a), u = Urgency(n, v);
                    if (u > score) { score = u; best = n; value = v; }
                }
                a.NextDemand = frame + DemandFrames / 4;
                if (best == Need.None) return;
                a.Demand = best;
                a.Baseline = value;
                a.Deadline = frame + DemandFrames;
                Chirps.Send("Demand", 0, Lang.T(NeedKey(best)));
                return;
            }
            bool met = a.Demand == Need.NoDemolition ? !a.Disturbed && (int)(frame - a.Deadline) >= 0 : Satisfied(a.Demand, Measure(a.Demand, a), a.Baseline);
            bool failed = a.Demand == Need.NoDemolition ? a.Disturbed : (int)(frame - a.Deadline) >= 0;
            if (met)
            {
                a.Gripe = Mathf.Max(0f, a.Gripe - MetRelief);
                a.Met = frame + MetFrames;
                Chirps.Send("DemandMet", 0, Lang.T(NeedKey(a.Demand)));
            }
            else if (failed)
            {
                a.Gripe = Mathf.Min(100f, a.Gripe + MissedAnger);
                a.Missed += 1f;
                Chirps.Send("DemandIgnored", 0, Lang.T(NeedKey(a.Demand)));
            }
            else return;
            a.Demand = Need.None;
            a.NextDemand = frame + DemandFrames / 2;
        }

        static string NeedKey(Need n) => "need_" + n.ToString().ToLowerInvariant();

        internal static bool Info(byte district, out float strength, out float gripe, out Need demand, out bool strike)
        {
            Association a = districts ? (district == 0 ? null : all[district]) : all[0];
            bool active = a != null && a.Active;
            strength = active ? a.Strength : 0f;
            gripe = active ? a.Gripe : 0f;
            demand = active ? a.Demand : Need.None;
            strike = active && a.Strike;
            return active;
        }

        internal static string NeedText(Need n) => Lang.T(NeedKey(n));
        internal static bool HasDistricts => districts;

        internal static string Describe(byte district)
        {
            if (districts && district == 0) return Lang.T("assoc_outside");
            Association a = all[districts ? district : 0];
            string title = Lang.T("assoc_title");
            if (!a.Active) return title + "\n" + Lang.T(a.Forming != 0 ? "assoc_forming" : "assoc_none");
            var s = new System.Text.StringBuilder(title);
            s.Append('\n').Append(string.Format(Lang.T("assoc_strength"), Mathf.RoundToInt(a.Strength)));
            s.Append('\n').Append(string.Format(Lang.T("assoc_gripe"), Mathf.RoundToInt(a.Gripe)));
            if (a.Demand != Need.None) s.Append('\n').Append(Lang.F(Lang.T("assoc_demand"), Lang.T(NeedKey(a.Demand)), Math.Max(0, (int)(a.Deadline - Sim.Frame) / (int)Week)));
            if (a.Strike) s.Append('\n').Append(Lang.T("assoc_strike"));
            return s.ToString();
        }

        internal static void Save(System.IO.BinaryWriter w)
        {
            w.Write(districts);
            w.Write(government);
            foreach (Association a in all)
            {
                w.Write((byte)((a.Active ? 1 : 0) | (a.Strike ? 2 : 0) | (a.Disturbed ? 4 : 0)));
                w.Write(a.Forming); w.Write(a.Deadline); w.Write(a.NextDemand); w.Write(a.Party); w.Write(a.Met);
                w.Write(a.Strength); w.Write(a.Gripe); w.Write(a.Displaced); w.Write(a.Projects); w.Write(a.Missed); w.Write(a.Baseline);
                w.Write(a.Burden); w.Write(a.Notices); w.Write(a.Tenure);
                w.Write((byte)a.Demand); w.Write(a.Calm); w.Write(a.Homes);
                w.Write(a.Center.x); w.Write(a.Center.z);
            }
        }

        internal static void Load(System.IO.BinaryReader r)
        {
            districts = r.ReadBoolean();
            government = r.ReadInt32();
            for (int i = 0; i < Districts; i++)
            {
                var a = new Association();
                byte f = r.ReadByte();
                a.Active = (f & 1) != 0; a.Strike = (f & 2) != 0; a.Disturbed = (f & 4) != 0;
                a.Forming = r.ReadUInt32(); a.Deadline = r.ReadUInt32(); a.NextDemand = r.ReadUInt32(); a.Party = r.ReadUInt32(); a.Met = r.ReadUInt32();
                a.Strength = r.ReadSingle(); a.Gripe = r.ReadSingle(); a.Displaced = r.ReadSingle(); a.Projects = r.ReadSingle(); a.Missed = r.ReadSingle(); a.Baseline = r.ReadSingle();
                a.Burden = r.ReadSingle(); a.Notices = r.ReadSingle(); a.Tenure = r.ReadSingle();
                a.Demand = (Need)r.ReadByte(); a.Calm = r.ReadInt32(); a.Homes = r.ReadInt32();
                a.Center = new Vector3(r.ReadSingle(), 0f, r.ReadSingle());
                all[i] = a;
            }
        }
    }

    static class Tents
    {
        static volatile PropInfo[] infos = new PropInfo[0];
        static volatile string[] wanted = new string[0];
        static readonly bool[] mine = new bool[PropManager.MAX_PROP_COUNT];
        static readonly List<ushort> placed = new List<ushort>();
        static float spacing = 4.5f;

        internal static int Count => placed.Count;

        internal static int Kinds => infos.Length;

        internal static float Spacing => spacing;

        internal static void Reset()
        {
            string raw = Settings.TentProp.value ?? "";
            var names = new List<string>();
            foreach (string part in raw.Split(',', ';', '\n'))
            {
                string t = part.Trim().Trim('"');
                if (t.Length != 0) names.Add(t);
            }
            string[] w = names.ToArray();
            var matched = new bool[w.Length];
            var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int k = 0; k < w.Length; k++)
                if (Digits(w[k], w[k].Length)) { packages.Add(w[k]); matched[k] = true; }
            if (w.Length != 0)
            {
                try
                {
                    foreach (ColossalFramework.Packaging.Package pkg in ColossalFramework.Packaging.PackageManager.allPackages)
                    {
                        if (pkg == null || string.IsNullOrEmpty(pkg.packageName)) continue;
                        string main = pkg.packageMainAsset, file = string.IsNullOrEmpty(pkg.packagePath) ? null : Path.GetFileNameWithoutExtension(pkg.packagePath);
                        for (int k = 0; k < w.Length; k++)
                            if (Same(w[k], main) || Same(w[k], file)) { packages.Add(pkg.packageName); matched[k] = true; }
                    }
                }
                catch (Exception e) { Debug.LogWarning("[City Consequences] Package lookup failed: " + e.Message); }
            }
            var found = new List<PropInfo>();
            uint total = (uint)PrefabCollection<PropInfo>.LoadedCount();
            for (uint i = 0; i < total; i++)
            {
                PropInfo p = PrefabCollection<PropInfo>.GetLoaded(i);
                if (!Usable(p)) continue;
                if (w.Length == 0) { if (Word(p.name)) found.Add(p); continue; }
                int dot = p.name.IndexOf('.');
                if (dot > 0 && packages.Contains(p.name.Substring(0, dot))) { found.Add(p); continue; }
                string clean = Clean(p.name);
                for (int k = 0; k < w.Length; k++)
                    if (Same(p.name, w[k]) || Same(clean, w[k])) { found.Add(p); matched[k] = true; break; }
            }
            for (int k = 0; k < w.Length; k++)
            {
                if (matched[k]) continue;
                for (uint i = 0; i < total; i++)
                {
                    PropInfo p = PrefabCollection<PropInfo>.GetLoaded(i);
                    if (!Usable(p) || p.name.IndexOf(w[k], StringComparison.OrdinalIgnoreCase) < 0 || found.Contains(p)) continue;
                    found.Add(p);
                    matched[k] = true;
                }
                if (!matched[k]) Debug.LogWarning("[City Consequences] No loaded prop matches tent entry \"" + w[k] + "\"");
            }
            float size = 0f;
            foreach (PropInfo p in found)
                if (p.m_generatedInfo != null) size = Mathf.Max(size, Mathf.Max(p.m_generatedInfo.m_size.x, p.m_generatedInfo.m_size.z) * p.m_maxScale);
            spacing = Mathf.Clamp(size + 1f, 4.5f, 9f);
            wanted = w;
            infos = found.ToArray();
            var list = new System.Text.StringBuilder();
            for (int i = 0; i < found.Count && i < 20; i++) list.Append(i == 0 ? "" : ", ").Append(found[i].name);
            Debug.Log("[City Consequences] Tent props (" + found.Count + ", spacing " + spacing.ToString("0.0") + " m): " + (found.Count == 0 ? "none found" : list.ToString()));
        }

        static bool Usable(PropInfo p) => p != null && p.name != null && p.m_class != null && p.m_prefabInitialized && (p.m_mesh != null || p.m_lodMesh != null);

        static bool Same(string a, string b) => a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        static bool Digits(string n, int length)
        {
            if (length <= 0) return false;
            for (int i = 0; i < length; i++) if (!char.IsDigit(n[i])) return false;
            return true;
        }

        static string Clean(string n)
        {
            int dot = n.IndexOf('.');
            if (dot > 0 && Digits(n, dot)) n = n.Substring(dot + 1);
            return n.EndsWith("_Data", StringComparison.Ordinal) ? n.Substring(0, n.Length - 5) : n;
        }

        static bool Word(string n)
        {
            int i = n.IndexOf("tent", StringComparison.OrdinalIgnoreCase);
            while (i >= 0)
            {
                if (i == 0 || !char.IsLetter(n[i - 1])) return true;
                i = n.IndexOf("tent", i + 4, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        static bool Listed(PropInfo p)
        {
            if (p == null || p.name == null) return false;
            PropInfo[] all = infos;
            for (int i = 0; i < all.Length; i++) if (all[i] == p) return true;
            return Word(p.name);
        }

        internal static bool Mine(ushort id) => mine[id];

        internal static bool CanPlace => infos.Length != 0 && placed.Count < (int)Settings.TentCap.value;

        static bool Room(Vector3 position)
        {
            PropInstance[] props = Singleton<PropManager>.instance.m_props.m_buffer;
            float min = spacing * 0.65f;
            min *= min;
            for (int i = 0; i < placed.Count; i++)
                if (VectorUtils.LengthSqrXZ(props[placed[i]].Position - position) < min) return false;
            return true;
        }

        internal static ushort Place(Vector3 position, float angle)
        {
            PropInfo[] all = infos;
            if (all.Length == 0 || placed.Count >= (int)Settings.TentCap.value || !Room(position)) return 0;
            PropInfo p = all[all.Length == 1 ? 0 : Sim.Roll((uint)all.Length)];
            if (!Singleton<PropManager>.instance.CreateProp(out ushort id, ref Singleton<SimulationManager>.instance.m_randomizer, p, position, angle, true)) return 0;
            mine[id] = true;
            placed.Add(id);
            return id;
        }

        static bool Owns(ushort id)
        {
            if (id == 0) return false;
            PropInstance[] props = Singleton<PropManager>.instance.m_props.m_buffer;
            if ((props[id].m_flags & (ushort)PropInstance.Flags.Created) == 0) return false;
            return mine[id] || Listed(props[id].Info);
        }

        internal static void Clear()
        {
            for (int i = 0; i < placed.Count; i++) mine[placed[i]] = false;
            placed.Clear();
        }

        internal static void Discard(ushort id)
        {
            if (Owns(id)) Singleton<PropManager>.instance.ReleaseProp(id);
        }

        internal static void Remove(ushort id)
        {
            if (!Owns(id)) return;
            if (mine[id]) { mine[id] = false; placed.Remove(id); }
            Singleton<PropManager>.instance.ReleaseProp(id);
        }
    }

    static class Ui
    {
        const string Prefix = "CityConsequences", PublicName = Prefix + "Public", ShelterName = Prefix + "Shelter", ZonedBoxName = Prefix + "ZonedBox", ServiceBoxName = Prefix + "ServiceBox", ServesName = Prefix + "Serves", WorshipName = Prefix + "Worship";
        static readonly List<byte> servesMap = new List<byte>();
        static readonly FieldInfo target = typeof(WorldInfoPanel).GetField("m_InstanceID", BindingFlags.Instance | BindingFlags.NonPublic);
        static bool syncing;
        static ushort zoned, service;

        internal static void Zoned(ZonedBuildingWorldInfoPanel panel)
        {
            zoned = Target(panel);
            Sync(panel.component, zoned, true);
        }

        const string BadgeName = Prefix + "Badge", RentBadgeName = Prefix + "RentBadge";
        static readonly System.Reflection.FieldInfo icons = typeof(BuildingWorldInfoPanel).GetField("m_IconsContainer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance),
            banner = typeof(BuildingWorldInfoPanel).GetField("m_AllGood", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance),
            iconWidth = typeof(BuildingWorldInfoPanel).GetField("m_IconWidth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        internal static void Problems(BuildingWorldInfoPanel panel)
        {
            if (icons == null || banner == null || !(icons.GetValue(panel) is UIPanel strip)) return;
            ushort id = Target(panel);
            Building[] b = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            bool off = id == 0 || !Sim.Active || Icons.Atlas == null || (b[id].m_flags & (Building.Flags.Abandoned | Building.Flags.Collapsed | Building.Flags.BurnedDown)) != 0 || b[id].m_problems.IsFatal;
            int door = !off && Settings.Homeless.value ? Housing.DoorstepNow(id) : 0, rent = !off && Settings.Rent.value ? Housing.AlarmNow(id) : 0;
            UISprite a = Badge(strip, BadgeName, door >= Housing.DoorMinor, door >= Housing.DoorMajor ? Icons.Major : Icons.Minor, door >= Housing.DoorMajor ? "doorstep_major" : "doorstep_minor");
            UISprite c = Badge(strip, RentBadgeName, rent > 0, rent > 1 ? Icons.RentMajor : Icons.RentMinor, rent > 1 ? "rent_alarm_major" : "rent_alarm_minor");
            if (a == null && c == null) return;
            int ours = (a != null ? 1 : 0) + (c != null ? 1 : 0), others = 0;
            foreach (UIComponent x in strip.components) if (x != a && x != c && x.isVisible) others++;
            float step = iconWidth != null ? (float)iconWidth.GetValue(panel) : (a ?? c).width;
            if (banner.GetValue(panel) is UILabel label)
            {
                label.width = strip.width - strip.padding.horizontal - step * (others + ours);
                label.text = others + ours > 1 ? ColossalFramework.Globalization.Locale.Get("NOTIFICATION_MULTIPLE") : Lang.T(a != null ? "notif_doorstep_title" : "notif_rent_title");
            }
            if (others == 0) strip.color = door >= Housing.DoorMajor || rent > 1 ? panel.m_ProblemoBizarroMajorColor : panel.m_ProblemoBizarroNormalColor;
        }

        static UISprite Badge(UIPanel strip, string name, bool show, string sprite, string tip)
        {
            UISprite badge = strip.Find<UISprite>(name);
            if (!show)
            {
                if (badge != null && badge.isVisible) badge.isVisible = false;
                return null;
            }
            if (badge == null)
            {
                badge = strip.AddUIComponent<UISprite>();
                badge.name = name;
                badge.atlas = Icons.Atlas;
                UIComponent sample = strip.components.Count > 1 ? strip.components[0] : null;
                badge.size = sample != null && sample.width > 0f ? sample.size : new Vector2(36f, 36f);
            }
            badge.spriteName = sprite;
            badge.tooltip = Lang.T(tip);
            badge.isVisible = true;
            badge.opacity = 0.75f + Mathf.Cos(Time.time * Mathf.PI * 1.5f) * 0.25f;
            return badge;
        }

        internal static void Service(CityServiceWorldInfoPanel panel)
        {
            service = Target(panel);
            serviceRoot = panel.component;
            Sync(serviceRoot, service, false);
        }

        static UIComponent serviceRoot;

        internal static void Refresh()
        {
            try { if (serviceRoot != null && serviceRoot.isVisible && service != 0) Sync(serviceRoot, service, false); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static ushort Target(WorldInfoPanel panel) => target != null && target.GetValue(panel) is InstanceID id ? id.Building : (ushort)0;

        static void Sync(UIComponent root, ushort id, bool isZoned)
        {
            if (id == 0 || !Sim.Active || root == null) return;
            UIPanel box = Box(root, isZoned);
            UICheckBox shelter = Toggle(box, ShelterName, "tog_shelter", "tog_shelter_tip", v => { ushort b = isZoned ? zoned : service; if (v) Uncheck(box, PublicName); Act(() => Housing.SetShelter(b, v)); });
            UICheckBox pub = isZoned ? Toggle(box, PublicName, "tog_public", "tog_public_tip", v => { ushort b = zoned; if (v) Uncheck(box, ShelterName); Act(() => Housing.SetPublic(b, v)); }) : null;
            syncing = true;
            if (shelter != null)
            {
                BuildingInfo info = Singleton<BuildingManager>.instance.m_buildings.m_buffer[id].Info;
                shelter.isVisible = Housing.IsShelter(id) || (isZoned ? Housing.IsResidential(id) || info != null && info.m_class.m_service == ItemClass.Service.Commercial : info != null && (info.m_buildingAI is ShelterAI || info.m_buildingAI is WorshipAI));
                shelter.isChecked = Housing.IsShelter(id);
            }
            if (pub != null)
            {
                pub.isVisible = Housing.IsResidential(id);
                bool forced = !Housing.MarkedPublic(id) && Housing.IsPublic(id);
                pub.isChecked = Housing.IsPublic(id);
                pub.isEnabled = !forced;
                Tip(pub, Lang.T(forced ? "tog_public_forced" : "tog_public_tip"));
            }
            if (!isZoned) Worship(box, id);
            syncing = false;
        }

        static void Worship(UIPanel box, ushort id)
        {
            BuildingInfo info = Singleton<BuildingManager>.instance.m_buildings.m_buffer[id].Info;
            bool worship = Religion.On && info != null && info.m_buildingAI is WorshipAI;
            UIPanel row = box.Find<UIPanel>(ServesName);
            if (row == null && worship)
            {
                row = box.AddUIComponent<UIPanel>();
                row.name = ServesName;
                row.size = new Vector2(box.width - 16f, 30f);
                UILabel label = row.AddUIComponent<UILabel>();
                label.text = Lang.T("rel_serves");
                label.textScale = 0.8125f;
                label.relativePosition = new Vector3(0f, 8f);
                UIDropDown dd = Dropdown(row, new string[0], row.width - 130f);
                dd.name = ServesName + "List";
                dd.relativePosition = new Vector3(128f, 1f);
                dd.eventSelectedIndexChanged += (c, i) =>
                {
                    if (syncing || service == 0 || i < 0 || i >= servesMap.Count) return;
                    Religion.SetCong(service, servesMap[i]);
                    Maps.Dirty = true;
                };
            }
            if (row != null)
            {
                ClosePopups(row);
                row.isVisible = worship;
                UIDropDown dd = row.Find<UIDropDown>(ServesName + "List");
                if (worship && dd != null)
                {
                    servesMap.Clear();
                    var items = new List<string>();
                    for (int g = 0; g < Religion.Slots; g++) if (Religion.C[g].Enabled) { servesMap.Add((byte)g); items.Add(Religion.C[g].Name); }
                    servesMap.Add(Religion.Interfaith);
                    items.Add(Lang.T("rel_interfaith"));
                    dd.items = items.ToArray();
                    dd.selectedIndex = Mathf.Max(0, servesMap.IndexOf(Religion.CongOf(id)));
                }
            }
            bool eligible = Religion.Enabled && Religion.Eligible(info);
            UICheckBox make = eligible || box.Find<UICheckBox>(WorshipName) != null ? Toggle(box, WorshipName, "rel_make", "rel_make_tip", v =>
            {
                BuildingInfo target = service != 0 ? Singleton<BuildingManager>.instance.m_buildings.m_buffer[service].Info : null;
                if (target == null) return;
                Settings.SetWorship(target.name, v);
                Religion.Apply(target, v);
            }) : null;
            if (make == null) return;
            make.isVisible = eligible;
            if (eligible) make.isChecked = Settings.IsWorship(info.name);
        }

        internal static UIDropDown Dropdown(UIComponent host, string[] items, float width)
        {
            UIDropDown dd = host.AddUIComponent<UIDropDown>();
            dd.size = new Vector2(width, 28f);
            dd.listBackground = "OptionsDropboxListbox";
            dd.itemHeight = 24;
            dd.itemHover = "ListItemHover";
            dd.itemHighlight = "ListItemHighlight";
            dd.normalBgSprite = "OptionsDropbox";
            dd.hoveredBgSprite = "OptionsDropboxHovered";
            dd.focusedBgSprite = "OptionsDropboxFocused";
            dd.listWidth = (int)width;
            dd.listHeight = 320;
            dd.foregroundSpriteMode = UIForegroundSpriteMode.Stretch;
            dd.popupColor = Color.white;
            dd.popupTextColor = new Color32(170, 170, 170, 255);
            dd.zOrder = 1;
            dd.textScale = 0.8f;
            dd.verticalAlignment = UIVerticalAlignment.Middle;
            dd.horizontalAlignment = UIHorizontalAlignment.Left;
            dd.textFieldPadding = new RectOffset(8, 0, 8, 0);
            dd.itemPadding = new RectOffset(14, 0, 8, 0);
            dd.items = items;
            dd.selectedIndex = items.Length > 0 ? 0 : -1;
            UIButton b = dd.AddUIComponent<UIButton>();
            dd.triggerButton = b;
            b.text = "";
            b.size = dd.size;
            b.relativePosition = Vector3.zero;
            b.normalFgSprite = "IconDownArrow";
            b.hoveredFgSprite = "IconDownArrowHovered";
            b.pressedFgSprite = "IconDownArrowPressed";
            b.focusedFgSprite = "IconDownArrowFocused";
            b.disabledFgSprite = "IconDownArrowDisabled";
            b.foregroundSpriteMode = UIForegroundSpriteMode.Fill;
            b.horizontalAlignment = UIHorizontalAlignment.Right;
            b.verticalAlignment = UIVerticalAlignment.Middle;
            b.zOrder = 0;
            return dd;
        }

        static void Act(Action a) => Singleton<SimulationManager>.instance.AddAction(a);

        static void Uncheck(UIPanel box, string name)
        {
            UICheckBox other = box.Find<UICheckBox>(name);
            if (other == null || !other.isChecked) return;
            syncing = true;
            other.isChecked = false;
            syncing = false;
        }

        static readonly FieldInfo popupField = typeof(UIDropDown).GetField("m_Popup", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        static readonly MethodInfo closePopup = FindClose();

        static MethodInfo FindClose()
        {
            foreach (MethodInfo m in typeof(UIDropDown).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name != "ClosePopup") continue;
                ParameterInfo[] p = m.GetParameters();
                if (p.Length == 0 || p.Length == 1 && p[0].ParameterType == typeof(bool)) return m;
            }
            return null;
        }

        internal static void ClosePopups(UIComponent root)
        {
            if (root == null) return;
            foreach (UIDropDown dd in root.GetComponentsInChildren<UIDropDown>(true)) Close(dd);
        }

        static void Close(UIDropDown dd)
        {
            if (dd == null) return;
            try
            {
                UIComponent popup = popupField != null ? popupField.GetValue(dd) as UIComponent : null;
                if (popupField != null && popup == null) return;
                if (closePopup != null) closePopup.Invoke(dd, closePopup.GetParameters().Length == 0 ? null : new object[] { false });
                else if (popup != null)
                {
                    popup.Hide();
                    UnityEngine.Object.Destroy(popup.gameObject);
                    popupField.SetValue(dd, null);
                }
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        static void Tip(UICheckBox check, string text)
        {
            if (check.tooltip == text) return;
            foreach (UIComponent c in check.GetComponentsInChildren<UIComponent>(true))
            {
                c.isTooltipLocalized = false;
                c.tooltipLocaleID = string.Empty;
                c.tooltip = text;
            }
        }

        static UICheckBox Template()
        {
            ZonedBuildingWorldInfoPanel panel = UIView.library.Get<ZonedBuildingWorldInfoPanel>(typeof(ZonedBuildingWorldInfoPanel).Name);
            return panel != null ? panel.component.Find<UICheckBox>("CheckboxHistorical") : null;
        }

        static UIPanel Box(UIComponent root, bool isZoned)
        {
            string name = isZoned ? ZonedBoxName : ServiceBoxName;
            UIPanel box = root.Find<UIPanel>(name);
            if (box != null) return box;
            box = root.AddUIComponent<UIPanel>();
            box.name = name;
            box.anchor = UIAnchorStyle.Top | UIAnchorStyle.Left;
            box.autoLayout = false;
            box.clipChildren = false;
            box.backgroundSprite = "GenericPanel";
            box.color = new Color32(70, 74, 78, 255);
            box.isInteractive = true;
            box.size = new Vector2(root.width, 0f);
            root.gameObject.AddComponent<PanelKeeper>().Init(root, box, isZoned);
            return box;
        }

        static UICheckBox Toggle(UIPanel box, string name, string text, string tip, Action<bool> changed)
        {
            UICheckBox check = box.Find<UICheckBox>(name);
            if (check != null) return check;
            UICheckBox template = Template();
            if (template == null) return null;
            GameObject go = UnityEngine.Object.Instantiate(template.gameObject);
            go.name = name;
            check = go.GetComponent<UICheckBox>();
            box.AttachUIComponent(go);
            check.anchor = UIAnchorStyle.Top | UIAnchorStyle.Left;
            check.isEnabled = true;
            check.isVisible = true;
            check.text = Lang.T(text);
            Tip(check, Lang.T(tip));
            check.eventCheckChanged += (c, v) => { if (!syncing) changed(v); };
            return check;
        }

        internal static void PoliciesBefore(PoliciesPanel panel)
        {
            foreach (string tab in new[] { "Taxation", "Services" })
            {
                UIComponent page = Page(panel, tab);
                if (page == null) continue;
                for (int i = page.components.Count - 1; i >= 0; i--)
                {
                    UIComponent c = page.components[i];
                    if (c.stringUserData == null || !c.stringUserData.StartsWith(Prefix)) continue;
                    page.RemoveUIComponent(c);
                    UnityEngine.Object.Destroy(c.gameObject);
                }
            }
        }

        internal static void PoliciesAfter(PoliciesPanel panel)
        {
            UIComponent services = Page(panel, "Services");
            if (services != null && Settings.Homeless.value) AddPolicy(panel, services, Housing.CriminalBit, "pol_criminal", "pol_criminal_desc", Icons.AntiHomeless, true);
            if (services != null && Religion.Enabled)
            {
                AddPolicy(panel, services, Religion.OutreachBit, "pol_outreach", "pol_outreach_desc", Icons.Outreach, true);
                AddPolicy(panel, services, Religion.InterfaithBit, "pol_interfaith", "pol_interfaith_desc", Icons.InterfaithPolicy, true);
            }
            UIComponent page = Page(panel, "Taxation");
            if (page == null) return;
            AddPolicy(panel, page, Housing.ControlBit, "pol_control", "pol_control_desc", "IconPolicyTaxLowerResHigh");
            AddPolicy(panel, page, Housing.SubsidyBit, "pol_subsidy", "pol_subsidy_desc", "IconPolicyTaxLowerResLow");
            AddPolicy(panel, page, Housing.PublicBit, "pol_public_all", "pol_public_all_desc", Icons.PublicHousing, true);
            AddPolicy(panel, page, Housing.BanBit, "pol_no_str", "pol_no_str_desc", Icons.NoShortStay, true);
            if (Religion.Enabled) AddPolicy(panel, page, Religion.TaxBit, "pol_worship_tax", "pol_worship_tax_desc", Icons.WorshipTax, true);
            UILabel status = page.AddUIComponent<UILabel>();
            status.name = status.stringUserData = Prefix + "Status";
            status.wordWrap = true;
            status.autoSize = false;
            status.autoHeight = true;
            status.width = Mathf.Max(200f, page.width - 20f);
            status.textScale = 0.8f;
            status.padding = new RectOffset(8, 8, 8, 8);
            status.gameObject.AddComponent<CommunityLabel>().Init(panel, status);
        }

        static UIComponent Page(PoliciesPanel panel, string tab)
        {
            UITabstrip strip = Traverse.Create(panel).Field("m_Tabstrip").GetValue<UITabstrip>();
            if (strip == null) return null;
            for (int k = 0; k < strip.tabCount && k < strip.tabPages.components.Count; k++)
                if (strip.tabs[k] is UIButton b && b.stringUserData == tab) return strip.tabPages.components[k];
            return null;
        }

        static void AddPolicy(PoliciesPanel panel, UIComponent page, byte bit, string name, string desc, string sprite, bool custom = false)
        {
            GameObject go = UITemplateManager.GetAsGameObject("PolicyTemplate");
            PolicyContainer vanilla = go.GetComponent<PolicyContainer>();
            if (vanilla != null) UnityEngine.Object.DestroyImmediate(vanilla);
            go.name = Prefix + bit;
            UIPanel entry = page.AttachUIComponent(go) as UIPanel;
            if (entry == null) return;
            entry.FitTo(entry.parent, LayoutDirection.Horizontal);
            entry.stringUserData = go.name;
            entry.objectUserData = panel;
            entry.isEnabled = true;
            entry.isVisible = true;
            UIButton button = entry.Find<UIButton>("PolicyButton");
            button.text = Lang.T(name);
            UIComponent box = Traverse.Create(panel).Property("policiesTooltip").GetValue<UIComponent>();
            if (box != null) button.tooltipBox = box;
            button.tooltip = ColossalFramework.DataBinding.TooltipHelper.Format("title", Lang.T(name), "text", string.Format(Lang.T(desc), Lang.TaxesTooHigh()));
            UISprite overlay = null;
            if (custom && Icons.Atlas != null)
            {
                button.normalFgSprite = button.disabledFgSprite = button.focusedFgSprite = button.hoveredFgSprite = button.pressedFgSprite = "";
                overlay = button.AddUIComponent<UISprite>();
                overlay.atlas = Icons.Atlas;
                overlay.spriteName = sprite;
                overlay.size = new Vector2(40f, 40f);
                overlay.isInteractive = false;
                overlay.relativePosition = new Vector3(Mathf.Max(2f, button.spritePadding.left), Mathf.Max(0f, (button.height - 40f) * 0.5f));
            }
            else
            {
                button.normalFgSprite = sprite;
                button.disabledFgSprite = sprite + "Disabled";
            }
            entry.Find<UISprite>("Unchecked").spriteName = "ToggleBase";
            entry.Find<UISprite>("Checked").spriteName = "ToggleBaseFocused";
            go.AddComponent<PolicyToggle>().Init(panel, entry.Find<UICheckBox>("Checkbox"), button, bit, overlay, sprite);
        }
    }

    public class PanelKeeper : MonoBehaviour
    {
        const float Pad = 8f, Gap = 4f;
        UIComponent root;
        UIPanel box;
        bool shown;

        internal void Init(UIComponent r, UIPanel b, bool isZoned)
        {
            root = r;
            box = b;
        }

        void OnDisable()
        {
            if (shown) Ui.ClosePopups(box);
            shown = false;
        }

        void LateUpdate()
        {
            if (root == null || box == null) return;
            if (!root.isVisible)
            {
                if (shown) Ui.ClosePopups(box);
                shown = false;
                return;
            }
            shown = true;
            float y = Pad;
            bool any = false;
            foreach (UIComponent c in box.components)
            {
                if (c == null || !c.isVisible) continue;
                c.relativePosition = new Vector3(Pad, y);
                y += c.height + Gap;
                any = true;
            }
            if (!any)
            {
                if (box.isVisible) box.isVisible = false;
                return;
            }
            if (!box.isVisible) box.isVisible = true;
            float bottom = root.height;
            foreach (UIComponent c in root.components)
                if (c != null && c != box && c.isVisible && c.relativePosition.y >= root.height - 1f) bottom = Mathf.Max(bottom, c.relativePosition.y + c.height);
            box.size = new Vector2(root.width, y - Gap + Pad);
            box.relativePosition = new Vector3(0f, bottom + Gap);
        }
    }

    public class StatsWindow : MonoBehaviour
    {
        const float Width = 640f, Height = 580f, Interval = 1f;
        const int MaxHouseholds = 24;
        static GameObject host;
        static readonly List<HouseholdView> views = new List<HouseholdView>();
        UIPanel panel;
        UILabel body;
        float next;

        internal static void Create()
        {
            if (host != null) return;
            host = new GameObject("CityConsequencesStats");
            host.AddComponent<StatsWindow>();
        }

        internal static void Remove()
        {
            if (host != null) Destroy(host);
            host = null;
        }

        void Update()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl), shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (ctrl && shift && Input.GetKeyDown(KeyCode.C)) Toggle();
            if (panel == null || !panel.isVisible || Time.unscaledTime < next) return;
            next = Time.unscaledTime + Interval;
            string text;
            try { text = Text(); }
            catch (Exception e) { text = e.Message; }
            if (body.text != text) body.text = text;
        }

        void Toggle()
        {
            if (panel == null) Build();
            if (panel == null) return;
            panel.isVisible = !panel.isVisible;
            if (!panel.isVisible) return;
            next = 0f;
            panel.BringToFront();
        }

        void Build()
        {
            UIView view = UIView.GetAView();
            if (view == null) return;
            panel = view.AddUIComponent(typeof(UIPanel)) as UIPanel;
            panel.name = "CityConsequencesStats";
            panel.backgroundSprite = "MenuPanel2";
            panel.size = new Vector2(Width, Height);
            panel.relativePosition = new Vector3((view.fixedWidth - Width) / 2f, (view.fixedHeight - Height) / 2f);
            UIDragHandle drag = panel.AddUIComponent<UIDragHandle>();
            drag.target = panel;
            drag.size = new Vector2(Width - 44f, 40f);
            drag.relativePosition = Vector3.zero;
            UILabel title = panel.AddUIComponent<UILabel>();
            title.text = Lang.T("dbg_title");
            title.textScale = 0.9f;
            title.relativePosition = new Vector3(12f, 12f);
            UIButton close = panel.AddUIComponent<UIButton>();
            close.normalBgSprite = "buttonclose";
            close.hoveredBgSprite = "buttonclosehover";
            close.pressedBgSprite = "buttonclosepressed";
            close.size = new Vector2(32f, 32f);
            close.relativePosition = new Vector3(Width - 38f, 4f);
            close.eventClick += (c, e) => panel.isVisible = false;
            UIScrollablePanel scroll = panel.AddUIComponent<UIScrollablePanel>();
            scroll.relativePosition = new Vector3(12f, 44f);
            scroll.size = new Vector2(Width - 24f, Height - 56f);
            scroll.clipChildren = true;
            scroll.autoLayout = false;
            scroll.scrollWheelDirection = UIOrientation.Vertical;
            scroll.builtinKeyNavigation = true;
            body = scroll.AddUIComponent<UILabel>();
            body.autoSize = false;
            body.autoHeight = true;
            body.wordWrap = true;
            body.width = scroll.width - 8f;
            body.textScale = 0.75f;
            body.relativePosition = Vector3.zero;
            panel.isVisible = false;
        }

        void OnDestroy()
        {
            if (panel != null) Destroy(panel.gameObject);
        }

        static string Money(double points) => (points * Housing.MoneyPerPoint).ToString("N2", LocaleManager.cultureInfo);
        static int Pct(double part, double whole) => whole > 0 ? (int)Math.Round(100.0 * part / whole) : 0;
        static string F(string id, params object[] args) => Lang.F(Lang.T(id), args);

        static int Percentile(int[] h, float q)
        {
            int total = 0;
            for (int i = 0; i < h.Length; i++) total += h[i];
            if (total == 0) return 0;
            int goal = Mathf.CeilToInt(total * q), sum = 0;
            for (int i = 0; i < h.Length; i++)
            {
                sum += h[i];
                if (sum >= goal) return i * Housing.BucketSize + Housing.BucketSize / 2;
            }
            return (h.Length - 1) * Housing.BucketSize;
        }

        static string Text()
        {
            if (!Sim.Active) return "";
            var s = new System.Text.StringBuilder();
            Tally c = Housing.Stats(Housing.CityTally);
            s.Append(Lang.T("dbg_city")).Append('\n');
            if (c.Households + c.Vacant == 0) s.Append(Lang.T("dbg_wait")).Append('\n');
            else
            {
                int[] h = Housing.Histogram;
                int renters = Math.Max(1, c.Households - c.Public);
                s.Append(F("dbg_households", c.Households, c.Vacant, Pct(c.Vacant, c.Households + c.Vacant))).Append('\n');
                s.Append(F("dbg_income", Money(c.Income / Math.Max(1, c.Households)), Money(Percentile(h, 0.2f)), Money(Percentile(h, 0.5f)), Money(Percentile(h, 0.8f)))).Append('\n');
                s.Append(F("dbg_rent", Money(c.Rent / renters), Pct(c.Shares, c.Counted))).Append('\n');
                s.Append(F("dbg_market", Housing.MarketFactor)).Append('\n');
                s.Append(F("dbg_notices", c.Notices, Pct(c.Public, c.Households))).Append('\n');
                s.Append(F("dbg_rich", c.Rich, c.Super)).Append('\n');
            }
            s.Append(F("dbg_homeless", Housing.StreetHomeless + Housing.CarHomeless, Housing.StreetHomeless, Housing.CarHomeless, Housing.Sheltered, Housing.StayingHomeless)).Append('\n');
            Records.Count();
            s.Append(F("dbg_records", Records.InJail, Records.Barred, Housing.JailedHomeless)).Append('\n');
            s.Append(F("dbg_tents", Tents.Count, (int)Settings.TentCap.value, Tents.Kinds)).Append('\n');
            s.Append(F("dbg_walk", Walk.Mean.ToString("0"), Roads.Network.ToString("0.00"))).Append("\n\n");
            Selected(s);
            s.Append(Lang.T("dbg_districts")).Append('\n');
            DistrictManager dm = Singleton<DistrictManager>.instance;
            for (int d = 0; d < 128; d++)
            {
                Tally t = Housing.Stats(d);
                if (t.Households + t.Vacant == 0) continue;
                string name = d == 0 ? Lang.T(Community.HasDistricts ? "dbg_nodistrict" : "dbg_citywide") : dm.GetDistrictName(d);
                string extra = "";
                if (Community.Info((byte)d, out float strength, out float gripe, out Need demand, out bool strike))
                {
                    extra = F("dbg_mood", Mathf.RoundToInt(strength), Mathf.RoundToInt(gripe));
                    if (demand != Need.None) extra += F("dbg_demand", Community.NeedText(demand));
                    if (strike) extra += Lang.T("dbg_strike");
                }
                s.Append(F("dbg_district", name, Money(t.Rent / Math.Max(1, t.Households - t.Public)), Money(t.Income / Math.Max(1, t.Households)), Pct(t.Shares, t.Counted), Pct(t.Vacant, t.Households + t.Vacant), extra)).Append('\n');
            }
            return s.ToString();
        }

        static void Selected(System.Text.StringBuilder s)
        {
            s.Append(Lang.T("dbg_building")).Append('\n');
            ushort b = WorldInfoPanel.GetCurrentInstanceID().Building;
            if (b == 0 || !Housing.IsHome(b))
            {
                s.Append(Lang.T("dbg_none")).Append("\n\n");
                return;
            }
            Vector3 p = Singleton<BuildingManager>.instance.m_buildings.m_buffer[b].m_position;
            s.Append(F("dbg_doorstep", Mathf.RoundToInt(Housing.DoorstepNow(b) * 100f / Housing.DoorMajor), Housing.StreetsNear(p).ToString("0.0"))).Append('\n');
            s.Append(F("dbg_building_rent", Money(Housing.TenantNow(b)), Money(Housing.MarketNow(b)), Housing.DiscountNow(b), Mathf.RoundToInt(Housing.DesirabilityNow(b) * 100f), Mathf.RoundToInt(Walk.Access(p)))).Append('\n');
            Housing.Households(b, views);
            for (int i = 0; i < views.Count && i < MaxHouseholds; i++)
            {
                HouseholdView v = views[i];
                string flags = (v.Tier == Housing.Super ? Lang.T("dbg_super") : v.Tier == Housing.Rich ? Lang.T("dbg_rich_tag") : "") + (v.Owner ? Lang.T("dbg_owner") : "") + (v.Notice ? Lang.T("dbg_notice") : "");
                s.Append(F("dbg_household", i + 1, v.People, Money(v.Income), Mathf.RoundToInt(v.Share * 100f), flags)).Append('\n');
            }
            if (views.Count > MaxHouseholds) s.Append(F("dbg_more", views.Count - MaxHouseholds)).Append('\n');
            s.Append('\n');
        }
    }

    public class CommunityLabel : MonoBehaviour
    {
        PoliciesPanel panel;
        UILabel label;
        float next;

        internal void Init(PoliciesPanel p, UILabel l)
        {
            panel = p;
            label = l;
        }

        void Update()
        {
            if (label == null || panel == null || !label.isVisible || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            string text = Sim.Active ? Community.Describe(panel.targetDistrict) : "";
            if (label.text != text) label.text = text;
        }
    }

    enum Rhythm : byte { Weekly, Daily, Festivals }
    enum Funeral : byte { Burial, Cremation, Either }
    enum Giving : byte { Tithing, Almsgiving, None }
    enum Institution : byte { Schools, Clinics, None }
    enum Conversion : byte { Missionary, Hereditary }
    enum Leisure : byte { Unrestricted, Austere }
    enum Family : byte { Small, Average, Large }
    enum EduNorm : byte { Equal, Scholarly, Traditional, Insular }

    sealed class Congregation
    {
        internal bool Enabled;
        internal string Name;
        internal int Share, Day;
        internal Rhythm Rhythm;
        internal Funeral Funeral;
        internal Giving Giving;
        internal Institution Institution;
        internal Conversion Conversion;
        internal Leisure Leisure;
        internal Family Family;
        internal EduNorm Education;
    }

    static class Religion
    {
        internal const int Slots = 6;
        internal const byte Unset = 254, Interfaith = 255, TaxBit = 8, OutreachBit = 16, InterfaithBit = 32;
        const int Count = Housing.Buildings, Steps = 256, Per = Count / Steps, Grid = 18, Cover = 512;
        const float Cell = 960f, Half = Grid * Cell * 0.5f, NeedPenalty = 5f, TaxPenalty = 2f, FestivalBonus = 4f, OutreachBonus = 5f, GiveUpTimer = 32;
        const Citizen.Flags EduMask = Citizen.Flags.Education1 | Citizen.Flags.Education2 | Citizen.Flags.Education3;
        internal static readonly Color[] Colors =
        {
            new Color(0.36f, 0.55f, 0.92f), new Color(0.93f, 0.62f, 0.20f), new Color(0.62f, 0.40f, 0.85f),
            new Color(0.25f, 0.72f, 0.62f), new Color(0.90f, 0.40f, 0.55f), new Color(0.70f, 0.66f, 0.30f)
        };
        internal static readonly Color InterfaithColor = new Color(0.96f, 0.90f, 0.70f);
        internal static readonly Congregation[] C = new Congregation[Slots];
        internal static int NonRelShare = 40;
        internal static bool Standalone;
        static uint seed;
        static readonly byte[] aff = new byte[CitizenManager.MAX_CITIZEN_COUNT], cong = new byte[Count], sat = new byte[Count * Slots], bsat = new byte[Count];
        static readonly bool[] brel = new bool[Count], outreach = new bool[Count];
        internal static readonly int[] Capacity = new int[Count], Served = new int[Count], Visitors = new int[Count];
        static readonly float[] ratio = new float[Count], demand = new float[Count];
        internal static volatile bool Ready;
        internal static int Residents;
        internal static readonly int[] Members = new int[Slots + 2];
        internal static readonly float[] GroupSat = new float[Slots];
        internal static float CitySat = 1f;
        static readonly int[] members = new int[Slots + 2], cnt = new int[Slots + 2], cov = new int[Cover], head = new int[Grid * Grid];
        static readonly float[] satSum = new float[Slots], weights = new float[Slots];
        static readonly List<ushort> wId = new List<ushort>();
        static readonly List<Vector3> wPos = new List<Vector3>();
        static readonly List<float> wR2 = new List<float>();
        static readonly List<byte> wCong = new List<byte>();
        static readonly List<bool> wOut = new List<bool>();
        static readonly List<int> link = new List<int>();
        static readonly bool[] festivalWas = new bool[Slots];
        static int residents, step, unmetCooldown, festivals;
        static float convYears;
        static DateTime lastTime;
        [ThreadStatic] internal static bool NoBaby;
        [ThreadStatic] internal static uint Locating;

        internal static bool Enabled => !Standalone && Settings.Faith != null && Settings.Faith.value;
        internal static bool On => Enabled && Sim.Active && C[0] != null;
        static float Ramp => Mathf.Clamp01((Residents - 1000) / 4000f);
        static Building[] B => Singleton<BuildingManager>.instance.m_buildings.m_buffer;

        internal static void Defaults(uint s)
        {
            seed = s;
            var r = new System.Random((int)s);
            NonRelShare = 40;
            int[] shares = { 30, 20, 10, 10, 10, 10 };
            for (int i = 0; i < Slots; i++)
                C[i] = new Congregation
                {
                    Enabled = i < 3,
                    Name = string.Format(Lang.T("rel_cong_default"), (char)('A' + i)),
                    Share = shares[i],
                    Day = r.Next(7),
                    Rhythm = (Rhythm)r.Next(3),
                    Funeral = (Funeral)r.Next(3),
                    Giving = (Giving)r.Next(3),
                    Institution = (Institution)r.Next(3),
                    Conversion = (Conversion)r.Next(2),
                    Leisure = (Leisure)r.Next(2),
                    Family = (Family)r.Next(3),
                    Education = (EduNorm)r.Next(4)
                };
            Array.Clear(aff, 0, aff.Length);
            for (int i = 0; i < cong.Length; i++) cong[i] = Unset;
        }

        static uint NewSeed() => (uint)(Environment.TickCount ^ Guid.NewGuid().GetHashCode()) | 1u;

        internal static void Start()
        {
            if (C[0] == null) Defaults(NewSeed());
            Ready = false;
            step = 0;
            convYears = 0f;
            lastTime = DateTime.MinValue;
            unmetCooldown = 0;
            for (int i = 0; i < Count; i++) ratio[i] = 1f;
            Array.Clear(Capacity, 0, Count);
            Array.Clear(Served, 0, Count);
            Array.Clear(Visitors, 0, Count);
            Array.Clear(brel, 0, Count);
            Array.Clear(outreach, 0, Count);
            Array.Clear(festivalWas, 0, Slots);
            festivals = 0;
        }

        internal static void Stop()
        {
            Ready = false;
            C[0] = null;
        }

        internal static float H(uint a, uint b)
        {
            uint x = a * 0x9E3779B1u ^ (b + seed) * 0x85EBCA77u;
            x ^= x >> 15; x *= 0x2C1B3C6Du; x ^= x >> 12; x *= 0x297A2D39u; x ^= x >> 15;
            return (x & 0xFFFFFF) / 16777216f;
        }

        static float Adherence(uint id)
        {
            float u = H(id, 7);
            return u < 0.25f ? 1f : u < 0.8f ? 0.65f : 0.15f;
        }

        static int G(uint id)
        {
            int a = aff[id] - 2;
            return a >= 0 && a < Slots && C[a] != null && C[a].Enabled ? a : -1;
        }

        internal static byte CongOf(ushort b) => cong[b];

        internal static void SetCong(ushort b, byte c) => cong[b] = c;

        internal static byte DefaultCong()
        {
            for (int i = 0; i < Slots; i++) if (C[i] != null && C[i].Enabled) return (byte)i;
            return Unset;
        }

        internal static string CongName(byte c) => c == Interfaith ? Lang.T("rel_interfaith") : c < Slots && C[c] != null ? C[c].Name : "-";

        internal static bool Festival(int g)
        {
            if (C[g] == null || C[g].Rhythm != Rhythm.Festivals) return false;
            int doy = Singleton<SimulationManager>.instance.m_currentGameTime.DayOfYear;
            for (int k = 0; k < 4; k++)
            {
                int s = (int)((seed % 365 + (uint)g * 53u + (uint)k * 91u) % 365u) + 1;
                if (doy >= s && doy < s + 2) return true;
            }
            return false;
        }

        static bool Has(Vector3 p, byte bit) => (Housing.PolicyAt(p) & bit) != 0;
        internal static bool Taxed(Vector3 p) => Has(p, TaxBit);
        internal static bool OutreachAt(Vector3 p) => Has(p, OutreachBit);
        internal static bool InterfaithAt(Vector3 p) => Has(p, InterfaithBit);

        internal static void Forget(uint c)
        {
            if (c < aff.Length) aff[c] = 0;
        }

        internal static void Released(ushort b)
        {
            cong[b] = Unset;
            Capacity[b] = Served[b] = Visitors[b] = 0;
        }

        internal static void Step()
        {
            if (step == 0) Begin();
            int end = (step + 1) * Per;
            for (int b = step * Per; b < end; b++) Scan((ushort)b);
            if (++step == Steps) { Finish(); step = 0; }
        }

        static int CellOf(float v) => Mathf.Clamp((int)((v + Half) / Cell), 0, Grid - 1);

        static void Begin()
        {
            wId.Clear(); wPos.Clear(); wR2.Clear(); wCong.Clear(); wOut.Clear(); link.Clear();
            for (int i = 0; i < head.Length; i++) head[i] = -1;
            Building[] buf = B;
            for (int i = 1; i < Count; i++)
            {
                if ((buf[i].m_flags & (Building.Flags.Created | Building.Flags.Deleted | Building.Flags.Completed | Building.Flags.Abandoned | Building.Flags.Collapsed)) != (Building.Flags.Created | Building.Flags.Completed)) continue;
                BuildingInfo info = buf[i].Info;
                if (info == null || !(info.m_buildingAI is WorshipAI)) continue;
                if (cong[i] == Unset) cong[i] = DefaultCong();
                byte c = cong[i];
                demand[i] = 0f;
                if (Capacity[i] <= 0 || c == Unset) continue;
                Vector3 p = buf[i].m_position;
                if (c == Interfaith ? !InterfaithAt(p) : c >= Slots || !C[c].Enabled) continue;
                float r = Radius(ref buf[i]);
                int cell = CellOf(p.z) * Grid + CellOf(p.x);
                link.Add(head[cell]);
                head[cell] = wId.Count;
                wId.Add((ushort)i); wPos.Add(p); wR2.Add(r * r); wCong.Add(c);
                wOut.Add(OutreachAt(p) || (c < Slots && C[c].Giving == Giving.Almsgiving));
            }
            Array.Clear(members, 0, members.Length);
            Array.Clear(satSum, 0, satSum.Length);
            residents = 0;
            float total = Mathf.Max(1, Residents);
            for (int c = 0; c < Slots; c++)
                weights[c] = C[c].Enabled ? Members[c + 2] / total * (C[c].Conversion == Conversion.Missionary ? 1f : 0.05f) : 0f;
        }

        internal static float Radius(ref Building b) => 200f + 20f * (b.Width + b.Length);
        internal static int BaseCapacity(ref Building b) => b.Width * b.Length * 40;

        static void Scan(ushort b)
        {
            brel[b] = false;
            outreach[b] = false;
            if (b == 0) return;
            ref Building bd = ref B[b];
            if ((bd.m_flags & (Building.Flags.Created | Building.Flags.Deleted | Building.Flags.Abandoned | Building.Flags.Collapsed)) != Building.Flags.Created) return;
            BuildingInfo info = bd.Info;
            if (info == null || info.m_class.m_service != ItemClass.Service.Residential) return;
            CitizenUnit[] units = Singleton<CitizenManager>.instance.m_units.m_buffer;
            Array.Clear(cnt, 0, cnt.Length);
            uint unit = bd.m_citizenUnits;
            for (int guard = 0; unit != 0 && guard < 524288; guard++)
            {
                if ((units[unit].m_flags & CitizenUnit.Flags.Home) != 0) Household(unit, ref units[unit]);
                unit = units[unit].m_nextUnit;
            }
            int n = 0;
            Vector3 pos = bd.m_position;
            if (wId.Count > 0)
            {
                int cx = CellOf(pos.x), cz = CellOf(pos.z);
                for (int z = Math.Max(0, cz - 1); z <= Math.Min(Grid - 1, cz + 1); z++)
                    for (int x = Math.Max(0, cx - 1); x <= Math.Min(Grid - 1, cx + 1); x++)
                        for (int k = head[z * Grid + x]; k >= 0 && n < Cover; k = link[k])
                        {
                            float dx = wPos[k].x - pos.x, dz = wPos[k].z - pos.z;
                            if (dx * dx + dz * dz > wR2[k]) continue;
                            cov[n++] = k;
                            if (wOut[k]) outreach[b] = true;
                        }
            }
            int religious = 0;
            float weighted = 0f;
            for (int g = 0; g < Slots; g++)
            {
                int m = cnt[g + 2];
                float s = 0f;
                if (m > 0)
                {
                    int own = 0, inter = 0;
                    float so = 0f, si = 0f;
                    for (int j = 0; j < n; j++)
                    {
                        int k = cov[j];
                        if (wCong[k] == g) { own++; so += ratio[wId[k]]; }
                        else if (wCong[k] == Interfaith) { inter++; si += ratio[wId[k]]; }
                    }
                    if (own > 0)
                    {
                        s = so / own;
                        float d = (float)m / own;
                        for (int j = 0; j < n; j++) if (wCong[cov[j]] == g) demand[wId[cov[j]]] += d;
                    }
                    else if (inter > 0)
                    {
                        s = 0.6f * si / inter;
                        float d = (float)m / inter;
                        for (int j = 0; j < n; j++) if (wCong[cov[j]] == Interfaith) demand[wId[cov[j]]] += d;
                    }
                    religious += m;
                    weighted += m * s;
                    satSum[g] += m * s;
                }
                sat[b * Slots + g] = (byte)(s * 255f);
            }
            for (int i = 1; i < cnt.Length; i++) members[i] += cnt[i];
            if (religious > 0)
            {
                brel[b] = true;
                bsat[b] = (byte)(weighted / religious * 255f);
            }
        }

        static void Household(uint unitId, ref CitizenUnit u)
        {
            Citizen[] buf = Singleton<CitizenManager>.instance.m_citizens.m_buffer;
            uint first = 0;
            for (int i = 0; i < 5 && first == 0; i++) { uint c = u.GetCitizen(i); if (c != 0 && !buf[c].Dead) first = c; }
            if (first == 0) return;
            if (aff[first] == 0 || (aff[first] >= 2 && G(first) < 0)) aff[first] = Pick(H(unitId, 11));
            byte ha = aff[first];
            if (convYears > 0f)
            {
                Citizen.AgeGroup age = Citizen.GetAgeGroup(buf[first].Age);
                if (age != Citizen.AgeGroup.Child && age != Citizen.AgeGroup.Teen)
                {
                    float src = ha == 1 ? 1f : (C[ha - 2].Conversion == Conversion.Hereditary ? 0.05f : 1f) * (1.3f - Adherence(first));
                    float pull = 0f;
                    for (int c = 0; c < Slots; c++) if (c + 2 != ha) pull += weights[c];
                    float p = 1f - Mathf.Exp(-0.05f * src * pull * convYears);
                    if (pull > 0f && Sim.Roll(1000000u) < p * 1000000f)
                    {
                        float x = Sim.Roll(1000000u) / 1000000f * pull;
                        int target = -1;
                        for (int c = 0; c < Slots; c++)
                        {
                            if (c + 2 == ha || weights[c] <= 0f) continue;
                            target = c;
                            x -= weights[c];
                            if (x <= 0f) break;
                        }
                        if (target >= 0)
                        {
                            for (int i = 0; i < 5; i++) { uint c = u.GetCitizen(i); if (c != 0 && aff[c] == ha) aff[c] = (byte)(target + 2); }
                            ha = (byte)(target + 2);
                        }
                    }
                }
            }
            for (int i = 0; i < 5; i++)
            {
                uint c = u.GetCitizen(i);
                if (c == 0 || buf[c].Dead) continue;
                if (aff[c] == 0) aff[c] = ha;
                else if (aff[c] >= 2 && G(c) < 0) aff[c] = 1;
                cnt[aff[c]]++;
                residents++;
            }
        }

        static byte Pick(float u)
        {
            float total = NonRelShare;
            for (int c = 0; c < Slots; c++) if (C[c].Enabled) total += C[c].Share;
            if (total <= 0f) return 1;
            float x = u * total;
            if (x < NonRelShare) return 1;
            x -= NonRelShare;
            for (int c = 0; c < Slots; c++)
            {
                if (!C[c].Enabled) continue;
                if (x < C[c].Share) return (byte)(c + 2);
                x -= C[c].Share;
            }
            return 1;
        }

        static void Finish()
        {
            for (int k = 0; k < wId.Count; k++)
            {
                ushort id = wId[k];
                float d = demand[id];
                ratio[id] = d > 0f ? Mathf.Min(1f, Capacity[id] / d) : 1f;
                Served[id] = (int)Mathf.Min(Capacity[id], d);
            }
            Residents = residents;
            int rel = 0;
            float total = 0f;
            for (int i = 0; i < members.Length; i++) Members[i] = members[i];
            for (int g = 0; g < Slots; g++)
            {
                GroupSat[g] = members[g + 2] > 0 ? satSum[g] / members[g + 2] : 1f;
                rel += members[g + 2];
                total += satSum[g];
            }
            CitySat = rel > 0 ? total / rel : 1f;
            DateTime now = Singleton<SimulationManager>.instance.m_currentGameTime;
            convYears = lastTime == DateTime.MinValue ? 0f : Mathf.Clamp((float)(now - lastTime).TotalDays / 365f, 0f, 0.5f);
            lastTime = now;
            Ready = true;
            int mask = 0;
            for (int g = 0; g < Slots; g++)
            {
                bool f = C[g].Enabled && Festival(g);
                if (f) mask |= 1 << g;
                if (f && !festivalWas[g] && members[g + 2] > 0) Chirps.Send("Festival", 0, C[g].Name);
                festivalWas[g] = f;
            }
            festivals = mask;
            if (unmetCooldown > 0) unmetCooldown--;
            else if (Ramp > 0.5f && CitySat < 0.4f) { Chirps.Send("WorshipFar", 0); unmetCooldown = 400; }
            if (Maps.Current == Maps.Kind.Religion) Maps.Dirty = true;
        }

        internal static ushort FindWorship(int g, Vector3 pos)
        {
            Building[] buf = B;
            ushort best = 0;
            float bd = float.MaxValue;
            int cx = CellOf(pos.x), cz = CellOf(pos.z);
            for (int z = Math.Max(0, cz - 1); z <= Math.Min(Grid - 1, cz + 1); z++)
                for (int x = Math.Max(0, cx - 1); x <= Math.Min(Grid - 1, cx + 1); x++)
                    for (int k = head[z * Grid + x]; k >= 0 && k < wId.Count; k = link[k])
                    {
                        byte c = wCong[k];
                        if (c != g && c != Interfaith) continue;
                        float dx = wPos[k].x - pos.x, dz = wPos[k].z - pos.z, d = dx * dx + dz * dz;
                        if (d > wR2[k]) continue;
                        if (c == Interfaith) d *= 2f;
                        ushort id = wId[k];
                        if ((buf[id].m_flags & Building.Flags.Created) == 0 || !(buf[id].Info?.m_buildingAI is WorshipAI)) continue;
                        if (d < bd) { bd = d; best = id; }
                    }
            return best;
        }

        internal static Color Paint(BuildingAI ai, ushort id, Color neutral, Color low, Color high)
        {
            if (ai is WorshipAI)
            {
                byte c = cong[id];
                Color col = c == Interfaith ? InterfaithColor : c < Slots ? Colors[c] : neutral;
                return Capacity[id] > 0 ? col : Color.Lerp(col, neutral, 0.6f);
            }
            return brel[id] ? Color.Lerp(low, high, bsat[id] / 255f) : neutral;
        }

        internal static uint Identify(ref Citizen c)
        {
            ushort home = c.m_homeBuilding;
            if (home == 0) return 0;
            Citizen[] z = Singleton<CitizenManager>.instance.m_citizens.m_buffer;
            CitizenUnit[] u = Singleton<CitizenManager>.instance.m_units.m_buffer;
            uint unit = B[home].m_citizenUnits;
            for (int guard = 0; unit != 0 && guard < 1024; guard++)
            {
                if ((u[unit].m_flags & CitizenUnit.Flags.Home) != 0)
                    for (int i = 0; i < 5; i++)
                    {
                        uint id = u[unit].GetCitizen(i);
                        if (id != 0 && z[id].m_flags == c.m_flags && z[id].m_age == c.m_age && z[id].m_family == c.m_family && z[id].m_workBuilding == c.m_workBuilding && z[id].m_instance == c.m_instance) return id;
                    }
                unit = u[unit].m_nextUnit;
            }
            return 0;
        }

        internal static float Devotion(uint id) => G(id) >= 0 ? Adherence(id) : 0f;

        internal static bool Grievances(uint id, ushort home, out float far, out float taxed)
        {
            far = taxed = 0f;
            int g = G(id);
            if (g < 0 || home == 0) return false;
            float a = Adherence(id);
            if (a < 0.5f) return false;
            if (Ready && B[home].Info?.m_class.m_service == ItemClass.Service.Residential) far = 0.6f * a * (1f - sat[home * Slots + g] / 255f) * Ramp;
            if (Taxed(B[home].m_position)) taxed = 0.45f * a;
            return far > 0f || taxed > 0f;
        }

        internal static bool Barred(uint id, int level)
        {
            int g = G(id);
            if (g < 0) return false;
            bool female = Citizen.GetGender(id) == Citizen.Gender.Female;
            float chance = 0f;
            switch (C[g].Education)
            {
                case EduNorm.Traditional: if (female) chance = level == 2 ? 0.35f : 0.6f; break;
                case EduNorm.Insular: if (level == 3) chance = 0.45f; break;
            }
            return chance > 0f && H(id, 100u + (uint)level) < chance * Adherence(id);
        }

        internal static Citizen.Flags Education(ref Citizen data) => data.m_flags & EduMask;

        internal static void Guard(uint id, ref Citizen data, Citizen.Flags before)
        {
            if (!On || id == 0) return;
            Citizen.Flags gained = data.m_flags & EduMask & ~before;
            if ((gained & Citizen.Flags.Education2) != 0 && Barred(id, 2)) data.m_flags &= ~(Citizen.Flags.Education2 | Citizen.Flags.Education3);
            else if ((gained & Citizen.Flags.Education3) != 0 && Barred(id, 3)) data.m_flags &= ~Citizen.Flags.Education3;
        }

        internal static void StudentOffer(TransferManager tm, TransferManager.TransferReason reason, TransferManager.TransferOffer offer)
        {
            if (offer.Citizen != 0 && On && reason >= TransferManager.TransferReason.Student1 && reason <= TransferManager.TransferReason.Student3)
            {
                if (reason == TransferManager.TransferReason.Student2 && Barred(offer.Citizen, 2)) return;
                if (reason == TransferManager.TransferReason.Student3 && Barred(offer.Citizen, 3)) return;
                int g = G(offer.Citizen);
                if (g >= 0 && C[g].Education == EduNorm.Scholarly) offer.Priority = Mathf.Min(7, offer.Priority + 3);
            }
            tm.AddOutgoingOffer(reason, offer);
        }

        internal static void Wellbeing(uint citizenID, ref Citizen data, Citizen.Flags before)
        {
            Guard(citizenID, ref data, before);
            ushort home = data.m_homeBuilding;
            if (!Ready || home == 0) return;
            BuildingInfo info = B[home].Info;
            if (info == null || info.m_class.m_service != ItemClass.Service.Residential) return;
            float w = 0f;
            int g = G(citizenID);
            if (g >= 0)
            {
                float a = Adherence(citizenID), s = sat[home * Slots + g] / 255f;
                w -= NeedPenalty * a * (1f - s) * Ramp;
                if (C[g].Giving == Giving.None && s >= 0.5f) w += 2f;
                if ((festivals & 1 << g) != 0) w += FestivalBonus * a;
                if (Taxed(B[home].m_position)) w -= TaxPenalty * a;
                if (C[g].Institution == Institution.Schools && s >= 0.5f)
                {
                    Citizen.AgePhase phase = Citizen.GetAgePhase(data.EducationLevel, data.Age);
                    if (phase < Citizen.AgePhase.Teen0 && !data.Education1 && Sim.Roll(1000u) < 15f * s) data.Education1 = true;
                    else if (phase >= Citizen.AgePhase.Teen0 && phase < Citizen.AgePhase.Young0 && data.Education1 && !data.Education2 && !Barred(citizenID, 2) && Sim.Roll(1000u) < 10f * s) data.Education2 = true;
                }
            }
            if (outreach[home] && data.Unemployed > 0) w += OutreachBonus;
            int r = Mathf.RoundToInt(w);
            if (r != 0) data.m_wellbeing = (byte)Mathf.Clamp(data.m_wellbeing + r, 0, 100);
        }

        internal static void Health(uint citizenID, ref Citizen data)
        {
            if (!Ready || !data.Sick || data.m_homeBuilding == 0) return;
            int g = G(citizenID);
            if (g < 0 || C[g].Institution != Institution.Clinics) return;
            float s = sat[data.m_homeBuilding * Slots + g] / 255f;
            if (s >= 0.5f && Sim.Roll(1000u) < 100f * s) data.Sick = false;
        }

        internal static bool Allowed(uint citizenID, TransferManager.TransferReason reason, ushort target)
        {
            if (target == 0 || !Housing.IsLeisure(reason)) return true;
            BuildingInfo info = B[target].Info;
            if (info == null || info.m_class.m_subService != ItemClass.SubService.CommercialLeisure) return true;
            int g = G(citizenID);
            if (g < 0 || C[g].Leisure != Leisure.Austere) return true;
            uint day = (uint)Singleton<SimulationManager>.instance.m_currentGameTime.DayOfYear;
            return H(citizenID, 1000u + day) >= 0.9f * Adherence(citizenID);
        }

        internal static bool Visit(HumanAI ai, uint citizenID, ushort source, TransferManager.TransferReason reason)
        {
            if (!Ready || !(ai is ResidentAI) || !Housing.IsLeisure(reason)) return true;
            ref Citizen c = ref Singleton<CitizenManager>.instance.m_citizens.m_buffer[citizenID];
            if (c.m_homeBuilding == 0 || (c.m_flags & Citizen.Flags.Tourist) != 0 || c.Sick) return true;
            int g = G(citizenID);
            if (g < 0) return true;
            SimulationManager sm = Singleton<SimulationManager>.instance;
            float a = Adherence(citizenID), hour = sm.m_currentDayTimeHour, chance;
            switch (C[g].Rhythm)
            {
                case Rhythm.Weekly: chance = (int)sm.m_currentGameTime.DayOfWeek == C[g].Day && hour >= 7f && hour < 14f ? 0.9f * a : 0.03f * a; break;
                case Rhythm.Daily: chance = hour >= 6f && hour < 21f ? 0.35f * a : 0f; break;
                default: chance = (festivals & 1 << g) != 0 ? (hour >= 8f && hour < 22f ? 0.95f * a : 0f) : 0.06f * a; break;
            }
            if (chance <= 0f || Sim.Roll(1000u) >= chance * 1000f) return true;
            ushort target = FindWorship(g, B[source].m_position);
            if (target == 0) return true;
            ai.StartTransfer(citizenID, ref c, reason, new TransferManager.TransferOffer { Building = target, Position = B[target].m_position, Amount = 1, Active = false });
            return c.m_visitBuilding != target;
        }

        static bool Compatible(uint id, bool crematorium)
        {
            int g = G(id);
            if (g < 0 || Adherence(id) < 0.2f) return true;
            switch (C[g].Funeral)
            {
                case Funeral.Burial: return !crematorium;
                case Funeral.Cremation: return crematorium;
                default: return true;
            }
        }

        static bool Waited(ushort building) => B[building].m_deathProblemTimer >= GiveUpTimer;

        internal static bool Dispatch(CemeteryAI cemetery, ushort target)
        {
            if (!Ready || target == 0 || Waited(target)) return true;
            bool crem = cemetery.m_graveCount == 0, any = false;
            CitizenManager cm = Singleton<CitizenManager>.instance;
            uint unit = B[target].m_citizenUnits;
            for (int guard = 0; unit != 0 && guard < 524288; guard++)
            {
                for (int i = 0; i < 5; i++)
                {
                    uint c = cm.m_units.m_buffer[unit].GetCitizen(i);
                    if (c == 0 || !cm.m_citizens.m_buffer[c].Dead || cm.m_citizens.m_buffer[c].GetBuildingByLocation() != target) continue;
                    if (Compatible(c, crem)) return true;
                    any = true;
                }
                unit = cm.m_units.m_buffer[unit].m_nextUnit;
            }
            return !any;
        }

        static readonly List<uint> heldIds = new List<uint>();
        static readonly List<Citizen.Location> heldLocations = new List<Citizen.Location>();

        internal static int HoldBodies(ref Vehicle data, ushort building)
        {
            int mark = heldIds.Count;
            if (!Ready || data.m_sourceBuilding == 0 || building == 0 || Waited(building)) return mark;
            if (!(B[data.m_sourceBuilding].Info?.m_buildingAI is CemeteryAI ai)) return mark;
            bool crem = ai.m_graveCount == 0;
            CitizenManager cm = Singleton<CitizenManager>.instance;
            uint unit = B[building].m_citizenUnits;
            for (int guard = 0; unit != 0 && guard < 524288; guard++)
            {
                for (int i = 0; i < 5; i++)
                {
                    uint c = cm.m_units.m_buffer[unit].GetCitizen(i);
                    if (c == 0 || !cm.m_citizens.m_buffer[c].Dead || cm.m_citizens.m_buffer[c].GetBuildingByLocation() != building || Compatible(c, crem)) continue;
                    heldIds.Add(c);
                    heldLocations.Add(cm.m_citizens.m_buffer[c].CurrentLocation);
                    cm.m_citizens.m_buffer[c].CurrentLocation = Citizen.Location.Moving;
                }
                unit = cm.m_units.m_buffer[unit].m_nextUnit;
            }
            return mark;
        }

        internal static void ReleaseBodies(int mark)
        {
            if (heldIds.Count <= mark) return;
            Citizen[] buf = Singleton<CitizenManager>.instance.m_citizens.m_buffer;
            for (int i = mark; i < heldIds.Count; i++) if (buf[heldIds[i]].m_vehicle == 0) buf[heldIds[i]].CurrentLocation = heldLocations[i];
            heldIds.RemoveRange(mark, heldIds.Count - mark);
            heldLocations.RemoveRange(mark, heldLocations.Count - mark);
        }

        static bool Fertile(ref CitizenUnit d) => d.m_citizen0 != 0 && d.m_citizen1 != 0 && (d.m_citizen2 == 0 || d.m_citizen3 == 0 || d.m_citizen4 == 0);

        static float Schooling(ref Citizen c)
        {
            float f;
            switch (c.EducationLevel)
            {
                case Citizen.Education.OneSchool: f = 0.95f; break;
                case Citizen.Education.TwoSchools: f = 0.85f; break;
                case Citizen.Education.ThreeSchools: f = 0.7f; break;
                default: f = 1f; break;
            }
            return Mathf.Max(0.05f, 1f - (1f - f) * Settings.BirthStrength.value / 100f);
        }

        internal static float Births(ref CitizenUnit d)
        {
            NoBaby = false;
            if (!Fertile(ref d)) return 1f;
            Citizen[] buf = Singleton<CitizenManager>.instance.m_citizens.m_buffer;
            uint mother = Citizen.GetGender(d.m_citizen1) == Citizen.Gender.Female && Citizen.GetGender(d.m_citizen0) != Citizen.Gender.Female ? d.m_citizen1 : d.m_citizen0;
            float m = 1f;
            int g = On && Ready ? G(mother) : -1;
            if (g >= 0)
            {
                float norm = C[g].Family == Family.Small ? 0.8f : C[g].Family == Family.Large ? 1.3f : 1f;
                m *= 1f + (norm - 1f) * Adherence(mother);
            }
            int mode = Settings.BirthMode.value;
            if (mode == 0) m *= Schooling(ref buf[mother]);
            else if (mode == 1) m *= (Schooling(ref buf[d.m_citizen0]) + Schooling(ref buf[d.m_citizen1])) * 0.5f;
            if (m < 1f && Sim.Roll(10000u) >= m * 10000f) NoBaby = true;
            return m;
        }

        internal static void ExtraBaby(ResidentAI ai, uint homeID, ref CitizenUnit data, float m)
        {
            NoBaby = false;
            if (m <= 1f || !Fertile(ref data)) return;
            CitizenManager cm = Singleton<CitizenManager>.instance;
            if (!ai.CanMakeBabies(data.m_citizen0, ref cm.m_citizens.m_buffer[data.m_citizen0]) || !ai.CanMakeBabies(data.m_citizen1, ref cm.m_citizens.m_buffer[data.m_citizen1])) return;
            ushort building = data.m_building;
            Vector3 pos = B[building].m_position;
            Singleton<ImmaterialResourceManager>.instance.CheckLocalResource(ImmaterialResourceManager.Resource.ChildCare, pos, out int local);
            uint range = local <= 0 ? 12u : 8u;
            if (Sim.Roll(range * 1000u) >= (m - 1f) * 1000f) return;
            SimulationManager sm = Singleton<SimulationManager>.instance;
            if (!cm.CreateCitizen(out uint child, 0, cm.m_citizens.m_buffer[data.m_citizen0].m_family, ref sm.m_randomizer)) return;
            cm.m_citizens.m_buffer[child].SetHome(child, 0, homeID);
            cm.m_citizens.m_buffer[child].m_flags |= Citizen.Flags.Original;
            aff[child] = aff[data.m_citizen0];
            if (building != 0)
            {
                DistrictManager dm = Singleton<DistrictManager>.instance;
                dm.m_districts.m_buffer[dm.GetDistrict(pos)].m_birthData.m_tempCount++;
            }
        }

        static readonly Type[] copyTypes = { typeof(PlayerBuildingAI), typeof(CommonBuildingAI), typeof(BuildingAI) };

        class Original
        {
            internal Type Type;
            internal readonly List<KeyValuePair<FieldInfo, object>> Fields = new List<KeyValuePair<FieldInfo, object>>();
        }

        static readonly Dictionary<BuildingInfo, Original> originals = new Dictionary<BuildingInfo, Original>();
        const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        internal static void Convert(BuildingInfo info) => Convert(info, false);

        static bool Convert(BuildingInfo info, bool live)
        {
            if (Standalone || info == null || info.m_class == null) return false;
            Settings.Init();
            if (!Settings.Faith.value || !Settings.IsWorship(info.name) || ItemClass.GetPrivateServiceIndex(info.m_class.m_service) != -1) return false;
            BuildingAI old = live ? info.m_buildingAI : info.GetComponent<BuildingAI>();
            if (old is WorshipAI) return false;
            WorshipAI ai = info.gameObject.AddComponent<WorshipAI>();
            if (old != null)
            {
                var keep = new Original { Type = old.GetType() };
                for (Type t = keep.Type; t != null && typeof(BuildingAI).IsAssignableFrom(t); t = t.BaseType)
                    foreach (FieldInfo f in t.GetFields(Declared))
                        if (!f.IsInitOnly && !f.IsLiteral) keep.Fields.Add(new KeyValuePair<FieldInfo, object>(f, f.GetValue(old)));
                originals[info] = keep;
                foreach (Type t in copyTypes)
                {
                    if (!t.IsInstanceOfType(old)) continue;
                    foreach (FieldInfo f in t.GetFields(Declared))
                        if (!f.IsInitOnly && !f.IsLiteral) f.SetValue(ai, f.GetValue(old));
                }
                if (live) UnityEngine.Object.Destroy(old);
                else UnityEngine.Object.DestroyImmediate(old);
            }
            info.m_buildingAI = ai;
            ai.m_info = info;
            return true;
        }

        static bool Revert(BuildingInfo info)
        {
            WorshipAI cur = info.m_buildingAI as WorshipAI;
            if (cur == null || !originals.TryGetValue(info, out Original keep)) return false;
            BuildingAI ai = info.gameObject.AddComponent(keep.Type) as BuildingAI;
            if (ai == null) return false;
            foreach (KeyValuePair<FieldInfo, object> f in keep.Fields) f.Key.SetValue(ai, f.Value);
            originals.Remove(info);
            info.m_buildingAI = ai;
            ai.m_info = info;
            UnityEngine.Object.Destroy(cur);
            return true;
        }

        internal static void Apply(BuildingInfo info, bool on)
        {
            if (info == null) return;
            bool changed;
            try { changed = on ? Convert(info, true) : Revert(info); }
            catch (Exception e) { Debug.LogException(e); return; }
            if (!changed) return;
            Singleton<SimulationManager>.instance.AddAction(() =>
            {
                try
                {
                    Building[] b = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
                    WorshipAI ai = info.m_buildingAI as WorshipAI;
                    for (int i = 1; i < b.Length; i++)
                    {
                        if ((b[i].m_flags & Building.Flags.Created) == 0 || b[i].Info != info) continue;
                        if (ai != null) ai.Attach((ushort)i, ref b[i]);
                        else Released((ushort)i);
                    }
                }
                catch (Exception e) { Debug.LogException(e); }
                ThreadHelper.dispatcher.Dispatch(() => Ui.Refresh());
            });
        }

        static readonly string[] keywords = { "church", "chapel", "cathedral", "basilica", "temple", "mosque", "masjid", "synagogue", "shrine", "pagoda", "monastery", "abbey", "gurdwara", "mandir", "kirche", "kapelle", "iglesia", "capilla", "eglise", "église", "chiesa", "igreja", "kosciol", "kościół", "cerkiew", "церковь", "храм", "собор", "教堂", "寺", "庙", "神社", "교회", "성당", "사찰" };

        internal static bool Eligible(BuildingInfo info)
        {
            if (info == null || !(info.m_buildingAI is PlayerBuildingAI) || ItemClass.GetPrivateServiceIndex(info.m_class.m_service) != -1) return false;
            if (info.m_buildingAI is WorshipAI || Settings.IsWorship(info.name) || Maps.Current == Maps.Kind.Religion) return true;
            Type t = info.m_buildingAI.GetType();
            if (t == typeof(PlayerBuildingAI) || t == typeof(ParkAI) || t == typeof(MonumentAI)) return true;
            string n = (info.name ?? "").ToLowerInvariant();
            foreach (string k in keywords) if (n.IndexOf(k, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        internal static byte[] Save()
        {
            if (C[0] == null) return null;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(2);
                w.Write(seed);
                w.Write(NonRelShare);
                foreach (Congregation c in C)
                {
                    w.Write(c.Enabled); w.Write(c.Name ?? ""); w.Write(c.Share); w.Write(c.Day);
                    w.Write((byte)c.Rhythm); w.Write((byte)c.Funeral); w.Write((byte)c.Giving); w.Write((byte)c.Institution);
                    w.Write((byte)c.Conversion); w.Write((byte)c.Leisure); w.Write((byte)c.Family); w.Write((byte)c.Education);
                }
                Building[] bm = B;
                int count = 0;
                for (int i = 1; i < Count; i++) if (cong[i] != Unset && (bm[i].m_flags & Building.Flags.Created) != 0) count++;
                w.Write(count);
                for (int i = 1; i < Count; i++) if (cong[i] != Unset && (bm[i].m_flags & Building.Flags.Created) != 0) { w.Write((ushort)i); w.Write(cong[i]); }
                int total = aff.Length / 2, p = 0;
                while (p < total)
                {
                    byte v = (byte)((aff[p * 2] & 15) | (aff[p * 2 + 1] & 15) << 4);
                    int n = 1;
                    while (p + n < total && n < 65535 && ((aff[(p + n) * 2] & 15) | (aff[(p + n) * 2 + 1] & 15) << 4) == v) n++;
                    w.Write(v); w.Write((ushort)n);
                    p += n;
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        internal static void Load(byte[] data, bool legacy)
        {
            Defaults(NewSeed());
            if (data == null || data.Length < 8) return;
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                int version = r.ReadInt32();
                if (version != (legacy ? 1 : 2)) return;
                Defaults(r.ReadUInt32());
                NonRelShare = r.ReadInt32();
                if (legacy)
                {
                    bool exempt = r.ReadBoolean(), help = r.ReadBoolean(), inter = r.ReadBoolean();
                    if (!exempt) Housing.SetPolicy(0, TaxBit, true);
                    if (help) Housing.SetPolicy(0, OutreachBit, true);
                    if (inter) Housing.SetPolicy(0, InterfaithBit, true);
                }
                foreach (Congregation c in C)
                {
                    c.Enabled = r.ReadBoolean(); c.Name = r.ReadString(); c.Share = r.ReadInt32(); c.Day = r.ReadInt32();
                    c.Rhythm = (Rhythm)Math.Min(2, (int)r.ReadByte()); c.Funeral = (Funeral)Math.Min(2, (int)r.ReadByte()); c.Giving = (Giving)Math.Min(2, (int)r.ReadByte()); c.Institution = (Institution)Math.Min(2, (int)r.ReadByte());
                    c.Conversion = (Conversion)Math.Min(1, (int)r.ReadByte()); c.Leisure = (Leisure)Math.Min(1, (int)r.ReadByte()); c.Family = (Family)Math.Min(2, (int)r.ReadByte()); c.Education = (EduNorm)Math.Min(3, (int)r.ReadByte());
                }
                for (int n = r.ReadInt32(); n > 0; n--) { ushort b = r.ReadUInt16(); byte v = r.ReadByte(); if (b < Count) cong[b] = v; }
                int p = 0, total = aff.Length / 2;
                while (p < total && r.BaseStream.Position < r.BaseStream.Length)
                {
                    byte v = r.ReadByte();
                    int k = r.ReadUInt16();
                    for (int i = 0; i < k && p < total; i++, p++) { aff[p * 2] = (byte)(v & 15); aff[p * 2 + 1] = (byte)(v >> 4); }
                }
            }
        }
    }

    public class WorshipAI : PlayerBuildingAI
    {
        static int VisitPlaces(ref Building b) => Mathf.Clamp(b.Width * b.Length * 2, 10, 300);

        public override void CreateBuilding(ushort buildingID, ref Building data)
        {
            base.CreateBuilding(buildingID, ref data);
            EnsureCitizenUnits(buildingID, ref data, 0, 0, VisitPlaces(ref data));
            Religion.SetCong(buildingID, Religion.DefaultCong());
            if (Religion.On) Chirps.Send("NewWorship", 0);
        }

        public override void BuildingLoaded(ushort buildingID, ref Building data, uint version)
        {
            base.BuildingLoaded(buildingID, ref data, version);
            EnsureCitizenUnits(buildingID, ref data, 0, 0, VisitPlaces(ref data));
        }

        internal void Attach(ushort id, ref Building data)
        {
            EnsureCitizenUnits(id, ref data, 0, 0, VisitPlaces(ref data));
            if (Religion.CongOf(id) == Religion.Unset) Religion.SetCong(id, Religion.DefaultCong());
        }

        public override void EndRelocating(ushort buildingID, ref Building data)
        {
            base.EndRelocating(buildingID, ref data);
            EnsureCitizenUnits(buildingID, ref data, 0, 0, VisitPlaces(ref data));
        }

        public override void ReleaseBuilding(ushort buildingID, ref Building data)
        {
            Religion.Released(buildingID);
            base.ReleaseBuilding(buildingID, ref data);
        }

        protected override void HandleWorkAndVisitPlaces(ushort buildingID, ref Building buildingData, ref Citizen.BehaviourData behaviour, ref int aliveWorkerCount, ref int totalWorkerCount, ref int workPlaceCount, ref int aliveVisitorCount, ref int totalVisitorCount, ref int visitPlaceCount)
        {
            visitPlaceCount += VisitPlaces(ref buildingData);
            GetVisitBehaviour(buildingID, ref buildingData, ref behaviour, ref aliveVisitorCount, ref totalVisitorCount);
        }

        protected override void ProduceGoods(ushort buildingID, ref Building buildingData, ref Building.Frame frameData, int productionRate, int finalProductionRate, ref Citizen.BehaviourData behaviour, int aliveWorkerCount, int totalWorkerCount, int workPlaceCount, int aliveVisitorCount, int totalVisitorCount, int visitPlaceCount)
        {
            base.ProduceGoods(buildingID, ref buildingData, ref frameData, productionRate, finalProductionRate, ref behaviour, aliveWorkerCount, totalWorkerCount, workPlaceCount, aliveVisitorCount, totalVisitorCount, visitPlaceCount);
            int cap = Religion.BaseCapacity(ref buildingData);
            Religion.Capacity[buildingID] = cap * finalProductionRate / 100;
            Religion.Visitors[buildingID] = aliveVisitorCount;
            if (!Religion.On || finalProductionRate == 0 || cap <= 0 || !Religion.Taxed(buildingData.m_position)) return;
            int income = GetMaintenanceCost() / 100 * Religion.Served[buildingID] / cap / 2;
            if (income > 0) Singleton<EconomyManager>.instance.AddResource(EconomyManager.Resource.PublicIncome, income, m_info.m_class);
        }

        protected override int AdjustMaintenanceCost(ushort buildingID, ref Building data, int maintenanceCost)
        {
            int cost = base.AdjustMaintenanceCost(buildingID, ref data, maintenanceCost);
            if (!Religion.On) return cost;
            float f = Religion.OutreachAt(data.m_position) ? 1.4f : 1f;
            byte c = Religion.CongOf(buildingID);
            if (c < Religion.Slots && Religion.C[c].Giving == Giving.Tithing)
                f -= 0.6f * Mathf.Clamp01(Religion.Served[buildingID] / (float)Mathf.Max(1, Religion.BaseCapacity(ref data)));
            return (int)(cost * Mathf.Max(0.2f, f));
        }

        public override string GetLocalizedStats(ushort buildingID, ref Building data)
        {
            if (!Religion.On) return base.GetLocalizedStats(buildingID, ref data);
            byte c = Religion.CongOf(buildingID);
            var s = new System.Text.StringBuilder();
            s.Append(string.Format(Lang.T("rel_s_cong"), Religion.CongName(c))).Append('\n');
            s.Append(Lang.F(Lang.T("rel_s_served"), Religion.Served[buildingID], Religion.Capacity[buildingID])).Append('\n');
            s.Append(Lang.F(Lang.T("rel_s_visit"), Religion.Visitors[buildingID]));
            if (c < Religion.Slots && Religion.Festival(c)) s.Append('\n').Append(Lang.T("rel_festival"));
            if (c == Religion.Interfaith && !Religion.InterfaithAt(data.m_position)) s.Append('\n').Append(Lang.T("rel_interfaith_off"));
            return s.ToString();
        }
    }

    static class Maps
    {
        internal enum Kind { None, Rent, Religion, Homeless }
        internal const InfoManager.InfoMode Host = InfoManager.InfoMode.BuildingLevel;
        const string HostPanel = "LevelsInfoViewPanel", ButtonName = "CityConsequencesMap";
        static readonly Kind[] kinds = { Kind.Rent, Kind.Homeless, Kind.Religion };
        internal static Kind Current;
        internal static volatile bool Dirty;
        internal static Color Neutral = Color.gray, Low = Color.yellow, High = Color.red, Poor = Color.gray, Good = Color.green;
        static readonly UIButton[] buttons = new UIButton[4];
        static readonly UISprite[] icons = new UISprite[4];
        static readonly UIPanel[] legends = new UIPanel[4];
        static GameInfoViewsPanel menu;
        static UIComponent hostPanel;
        static float next, retry;
        static readonly string[] states = { "", "Disabled", "Focused", "Hovered", "Pressed" };
        static readonly string[][] sprites = new string[4][];
        static readonly FieldInfo objectIndex = typeof(InfoViewsPanel).GetField("m_ObjectIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly MethodInfo select = typeof(InfoViewsPanel).GetMethod("SelectByIndex", BindingFlags.NonPublic | BindingFlags.Instance);

        static bool Available(Kind k) => k == Kind.Rent ? Settings.Rent.value : k == Kind.Homeless ? Settings.Homeless.value : Religion.Enabled;
        static string Icon(Kind k) => k == Kind.Rent ? Icons.Rent : k == Kind.Homeless ? Icons.HomelessInfo : Icons.Religion;
        static string Tip(Kind k) => Lang.T(k == Kind.Rent ? "map_rent" : k == Kind.Homeless ? "map_homeless" : "map_religion");

        internal static Color Paint(BuildingAI ai, ushort id, ref Building data, InfoManager.InfoMode mode, InfoManager.SubInfoMode sub)
        {
            if (Current == Kind.None || mode != Host) return ai.GetColor(id, ref data, mode, sub);
            if ((data.m_flags & (Building.Flags.Abandoned | Building.Flags.Collapsed | Building.Flags.BurnedDown)) != 0) return Neutral;
            return Current == Kind.Rent ? RentMap.Paint(id) : Current == Kind.Homeless ? HomelessMap.Paint(id, ref data) : Religion.Paint(ai, id, Neutral, Poor, Good);
        }

        static void Colors()
        {
            InfoProperties p = Singleton<InfoManager>.instance.m_properties;
            if (p == null) return;
            Neutral = p.m_neutralColor;
            InfoProperties.ModeProperties land = p.m_modeProperties[(int)InfoManager.InfoMode.LandValue];
            Low = land.m_activeColorB;
            High = land.m_activeColor;
            Good = p.m_modeProperties[(int)InfoManager.InfoMode.Health].m_activeColor;
            Poor = Color.Lerp(new Color(Neutral.r, Neutral.g, Neutral.b, Good.a), new Color(0.55f, 0.55f, 0.6f, Good.a), 0.6f);
        }

        internal static void Toggle(Kind k)
        {
            if (Current == k) { Stop(); Singleton<InfoManager>.instance.SetCurrentMode(InfoManager.InfoMode.None, InfoManager.SubInfoMode.Default); return; }
            if (Current != Kind.None) Legend(Current, false);
            ToolBase.OverrideInfoMode = true;
            WorldInfoPanel.HideAllWorldInfoPanels();
            Colors();
            if (k == Kind.Rent) RentMap.Measure();
            else if (k == Kind.Homeless) HomelessMap.Measure();
            Current = k;
            Singleton<InfoManager>.instance.SetCurrentMode(Host, InfoManager.SubInfoMode.Default);
            if (menu != null && select != null) select.Invoke(menu, new object[] { -1, false });
            Singleton<BuildingManager>.instance.UpdateBuildingColors();
            Legend(k, true);
            next = Time.unscaledTime + 5f;
        }

        internal static void Stop()
        {
            if (Current == Kind.None) return;
            Legend(Current, false);
            Current = Kind.None;
            if (Singleton<InfoManager>.instance.CurrentMode == Host) UIView.library.Show(HostPanel, true, true);
            Singleton<BuildingManager>.instance.UpdateBuildingColors();
        }

        internal static void Clear()
        {
            Current = Kind.None;
            for (int i = 0; i < legends.Length; i++)
            {
                if (legends[i] != null) UnityEngine.Object.Destroy(legends[i].gameObject);
                legends[i] = null;
                buttons[i] = null;
                icons[i] = null;
            }
            RentMap.Clear();
            HomelessMap.Clear();
            ReligionMap.Clear();
            menu = null;
            hostPanel = null;
        }

        internal static void Before(GameInfoViewsPanel panel)
        {
            foreach (Kind k in kinds)
            {
                UIButton old = panel.m_ChildContainer.Find<UIButton>(ButtonName + k);
                if (old != null)
                {
                    panel.m_ChildContainer.RemoveUIComponent(old);
                    UnityEngine.Object.Destroy(old.gameObject);
                }
                buttons[(int)k] = null;
                icons[(int)k] = null;
            }
        }

        internal static void After(GameInfoViewsPanel panel)
        {
            if (Icons.Atlas == null || objectIndex == null || panel == null || panel.m_ChildContainer == null) return;
            menu = panel;
            int count = (int)objectIndex.GetValue(panel), added = 0;
            UIButton last = null;
            foreach (Kind k in kinds)
            {
                if (!Available(k)) continue;
                UIButton button = panel.m_ChildContainer.Find<UIButton>(ButtonName + k);
                if (button == null)
                {
                    button = panel.m_ChildContainer.AttachUIComponent(UITemplateManager.GetAsGameObject("InfoViewButtonTemplate")) as UIButton;
                    if (button == null) return;
                    Kind kind = k;
                    button.name = ButtonName + k;
                    button.pivot = UIPivotPoint.TopCenter;
                    button.atlas = panel.m_Atlas;
                    button.text = string.Empty;
                    button.playAudioEvents = true;
                    button.tooltipAnchor = UITooltipAnchor.Anchored;
                    button.normalFgSprite = button.focusedFgSprite = button.hoveredFgSprite = button.pressedFgSprite = button.disabledFgSprite = string.Empty;
                    button.tooltip = Tip(k);
                    button.isEnabled = true;
                    button.eventClick += (c, p) => { p.Use(); Toggle(kind); };
                    UISprite icon = button.AddUIComponent<UISprite>();
                    icon.atlas = Icons.Atlas;
                    icon.spriteName = Icon(k);
                    icon.size = new Vector2(36f, 36f);
                    icon.isInteractive = false;
                    icon.relativePosition = new Vector3((button.width - 36f) * 0.5f, (button.height - 36f) * 0.5f);
                    icons[(int)k] = icon;
                }
                button.zOrder = count + added;
                buttons[(int)k] = button;
                last = button;
                added++;
            }
            if (last != null && panel.m_ChildContainer is UIPanel grid && panel.m_RootContainer != null)
                panel.m_RootContainer.height = Mathf.CeilToInt((count + added) / 2f) * (last.height + grid.autoLayoutPadding.top) + grid.autoLayoutPadding.top;
        }

        internal static bool Selected(GameInfoViewsPanel panel)
        {
            if (Current == Kind.None) return true;
            UIButton mine = buttons[(int)Current];
            foreach (UIComponent c in panel.m_ChildContainer.components)
                if (c is UIButton b && b != mine && b.state == UIButton.ButtonState.Focused) b.state = UIButton.ButtonState.Normal;
            return false;
        }

        internal static void Tick()
        {
            bool missing = false;
            foreach (Kind k in kinds) if (Available(k) && buttons[(int)k] == null) missing = true;
            if (missing && Time.unscaledTime >= retry)
            {
                retry = Time.unscaledTime + 2f;
                if (menu == null) menu = UnityEngine.Object.FindObjectOfType<GameInfoViewsPanel>();
                if (menu != null) After(menu);
            }
            InfoManager im = Singleton<InfoManager>.instance;
            if (Current != Kind.None && (im.CurrentMode != Host && im.NextMode != Host || !Available(Current))) { Legend(Current, false); Current = Kind.None; }
            foreach (Kind k in kinds)
            {
                UIButton button = buttons[(int)k];
                UISprite icon = icons[(int)k];
                if (button == null || icon == null) continue;
                bool on = Current == k;
                string[] names = sprites[(int)k];
                if (names == null)
                {
                    names = sprites[(int)k] = new string[states.Length];
                    for (int i = 0; i < states.Length; i++) names[i] = Icon(k) + states[i];
                }
                string sprite = names[!button.isEnabled ? 1 : on ? 2 : button.containsMouse ? (Input.GetMouseButton(0) ? 4 : 3) : 0];
                if (!ReferenceEquals(icon.spriteName, sprite) && icon.spriteName != sprite) icon.spriteName = sprite;
                UIButton.ButtonState want = on ? UIButton.ButtonState.Focused : button.containsMouse ? button.state : UIButton.ButtonState.Normal;
                if (button.state != want) button.state = want;
            }
            if (Current == Kind.None) return;
            if (hostPanel == null) hostPanel = UIView.library.Get(HostPanel);
            if (hostPanel != null && hostPanel.isVisible) hostPanel.isVisible = false;
            if (Dirty) { Dirty = false; Singleton<BuildingManager>.instance.UpdateBuildingColors(); }
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + (Current == Kind.Religion ? 1f : 5f);
            if (Current == Kind.Rent)
            {
                RentMap.Measure();
                RentMap.Refresh();
                Singleton<BuildingManager>.instance.UpdateBuildingColors();
            }
            else if (Current == Kind.Homeless)
            {
                HomelessMap.Measure();
                HomelessMap.Refresh();
                Singleton<BuildingManager>.instance.UpdateBuildingColors();
            }
            else ReligionMap.Refresh(legends[(int)Kind.Religion]);
        }

        internal static UIPanel Panel(string name, float height)
        {
            UIPanel legend = UIView.GetAView().AddUIComponent(typeof(UIPanel)) as UIPanel;
            legend.name = name;
            legend.backgroundSprite = "MenuPanel2";
            legend.size = new Vector2(260f, height);
            UIComponent host = UIView.library.Get(HostPanel);
            legend.absolutePosition = host != null ? host.absolutePosition : new Vector3(10f, 120f);
            return legend;
        }

        internal static void Scale(UIPanel legend, Color from, Color mid, Color to, bool split, float y)
        {
            for (int i = 0; i < 10; i++)
            {
                float t = (i + 0.5f) / 10f;
                UIPanel block = legend.AddUIComponent<UIPanel>();
                block.backgroundSprite = "GenericPanelWhite";
                block.size = new Vector2(23f, 14f);
                block.relativePosition = new Vector3(15f + i * 23f, y);
                Color c = split ? (t < 0.5f ? Color.Lerp(from, mid, t * 2f) : Color.Lerp(mid, to, t * 2f - 1f)) : Color.Lerp(from, to, t);
                block.color = new Color(c.r, c.g, c.b, 1f);
            }
        }

        internal static UILabel Label(UIComponent parent, string text, float scale, Vector3 at)
        {
            UILabel l = parent.AddUIComponent<UILabel>();
            l.text = text;
            l.textScale = scale;
            l.relativePosition = at;
            return l;
        }

        static void Legend(Kind k, bool show)
        {
            UIPanel legend = legends[(int)k];
            if (!show) { if (legend != null) legend.isVisible = false; return; }
            if (legend == null) legends[(int)k] = legend = k == Kind.Rent ? RentMap.Build() : k == Kind.Homeless ? HomelessMap.Build() : ReligionMap.Build();
            if (k == Kind.Rent) RentMap.Refresh();
            else if (k == Kind.Homeless) HomelessMap.Refresh();
            else ReligionMap.Refresh(legend);
            legend.isVisible = true;
            legend.BringToFront();
        }
    }

    static class RentMap
    {
        static readonly Color Public = new Color(0.36f, 0.55f, 0.86f);
        static float average;
        static int counted;
        static UILabel avgLabel;

        internal static Color Paint(ushort id)
        {
            if (!Housing.IsHome(id)) return Maps.Neutral;
            if (Housing.IsPublic(id)) return Public;
            int r = Housing.RentRaw(id);
            if (r <= 0) return Maps.Neutral;
            float t = Mathf.Clamp01(r / Housing.MaxRent);
            return t < 0.5f ? Color.Lerp(Maps.Neutral, Maps.Low, t * 2f) : Color.Lerp(Maps.Low, Maps.High, t * 2f - 1f);
        }

        internal static void Measure()
        {
            double sum = 0;
            int n = 0;
            for (ushort id = 1; id < Housing.Buildings; id++)
            {
                if (!Housing.IsHome(id) || Housing.IsPublic(id)) continue;
                int r = Housing.RentRaw(id);
                if (r <= 0) continue;
                n++;
                sum += r;
            }
            counted = n;
            average = n == 0 ? 0f : (float)(sum / n);
        }

        internal static void Clear() => avgLabel = null;

        static string Money(float points) => (points * Housing.MoneyPerPoint).ToString("N2", LocaleManager.cultureInfo);

        static string Text() => counted == 0 ? Lang.T("map_none") : string.Format(Lang.T("map_avg"), Money(average)) + "\n" + string.Format(Lang.T("map_top"), Money(Housing.MaxRent));

        internal static UIPanel Build()
        {
            UIPanel legend = Maps.Panel("CityConsequencesRentLegend", 150f);
            Maps.Label(legend, Lang.T("map_rent"), 1f, new Vector3(12f, 12f));
            Maps.Scale(legend, Maps.Neutral, Maps.Low, Maps.High, true, 44f);
            Maps.Label(legend, Lang.T("map_low"), 0.75f, new Vector3(15f, 62f));
            Maps.Label(legend, Lang.T("map_high"), 0.75f, new Vector3(205f, 62f));
            UIPanel pub = legend.AddUIComponent<UIPanel>();
            pub.backgroundSprite = "GenericPanelWhite";
            pub.size = new Vector2(14f, 14f);
            pub.color = Public;
            pub.relativePosition = new Vector3(15f, 84f);
            Maps.Label(legend, Lang.T("map_public"), 0.75f, new Vector3(35f, 84f));
            avgLabel = Maps.Label(legend, "", 0.75f, new Vector3(15f, 106f));
            return legend;
        }

        internal static void Refresh()
        {
            if (avgLabel != null) avgLabel.text = Text();
        }
    }

    static class HomelessMap
    {
        internal static readonly Color Shelter = new Color(0.36f, 0.55f, 0.86f);
        static readonly float[] streets = new float[Housing.GridCells];
        static UILabel stats;

        internal static void Measure() => Housing.CopyStreets(streets);

        internal static Color Paint(ushort id, ref Building data)
        {
            if ((data.m_flags & (Building.Flags.Created | Building.Flags.Deleted)) != Building.Flags.Created) return Maps.Neutral;
            if (Housing.IsShelter(id)) return Shelter;
            float t = Mathf.Clamp01(Housing.StreetsIn(streets, data.m_position) / Housing.MapFull);
            if (t <= 0.02f) return Maps.Neutral;
            return t < 0.5f ? Color.Lerp(Maps.Neutral, Maps.Low, t * 2f) : Color.Lerp(Maps.Low, Maps.High, t * 2f - 1f);
        }

        internal static void Clear() => stats = null;

        internal static UIPanel Build()
        {
            UIPanel legend = Maps.Panel("CityConsequencesHomelessLegend", 196f);
            Maps.Label(legend, Lang.T("map_homeless"), 1f, new Vector3(12f, 12f));
            Maps.Scale(legend, Maps.Neutral, Maps.Low, Maps.High, true, 44f);
            Maps.Label(legend, Lang.T("map_hl_low"), 0.75f, new Vector3(15f, 62f));
            UILabel hi = Maps.Label(legend, Lang.T("map_hl_high"), 0.75f, Vector3.zero);
            hi.relativePosition = new Vector3(245f - hi.width, 62f);
            UIPanel shelter = legend.AddUIComponent<UIPanel>();
            shelter.backgroundSprite = "GenericPanelWhite";
            shelter.size = new Vector2(14f, 14f);
            shelter.color = Shelter;
            shelter.relativePosition = new Vector3(15f, 84f);
            Maps.Label(legend, Lang.T("tog_shelter"), 0.75f, new Vector3(35f, 84f));
            stats = Maps.Label(legend, "", 0.75f, new Vector3(15f, 106f));
            return legend;
        }

        internal static void Refresh()
        {
            if (stats == null) return;
            int car = Housing.CarHomeless, total = car + Housing.StreetHomeless;
            string text = Lang.F(Lang.T("map_hl_total"), total) + "\n" + Lang.F(Lang.T("map_hl_shelters"), Housing.Sheltered) + "\n" + Lang.F(Lang.T("map_hl_cars"), car) + "\n" + Lang.F(Lang.T("map_hl_jail"), Housing.JailedHomeless);
            if (stats.text != text) stats.text = text;
            if (stats.parent is UIPanel legend) legend.height = stats.relativePosition.y + stats.height + 12f;
        }
    }

    static class ReligionMap
    {
        static UILabel list, totals;
        static UIButton edit;
        static string shown;

        internal static void Clear()
        {
            list = totals = null;
            edit = null;
            shown = null;
            Congregations.Remove();
        }

        static string Hex(Color c) => ((int)(c.r * 255)).ToString("X2") + ((int)(c.g * 255)).ToString("X2") + ((int)(c.b * 255)).ToString("X2");

        internal static UIPanel Build()
        {
            UIPanel legend = Maps.Panel("CityConsequencesReligionLegend", 200f);
            Maps.Label(legend, Lang.T("map_religion"), 1f, new Vector3(12f, 12f));
            Maps.Scale(legend, Maps.Poor, Maps.Poor, Maps.Good, false, 44f);
            Maps.Label(legend, Lang.T("map_rel_low"), 0.75f, new Vector3(15f, 62f));
            UILabel hi = Maps.Label(legend, Lang.T("map_rel_high"), 0.75f, new Vector3(0f, 62f));
            hi.relativePosition = new Vector3(245f - hi.width, 62f);
            UILabel note = Maps.Label(legend, Lang.T("map_rel_worship"), 0.7f, new Vector3(15f, 84f));
            note.autoSize = false;
            note.autoHeight = true;
            note.wordWrap = true;
            note.width = 230f;
            list = Maps.Label(legend, "", 0.75f, new Vector3(15f, note.relativePosition.y + note.height + 6f));
            list.processMarkup = true;
            totals = Maps.Label(legend, "", 0.7f, Vector3.zero);
            totals.autoSize = false;
            totals.autoHeight = true;
            totals.wordWrap = true;
            totals.width = 230f;
            edit = legend.AddUIComponent<UIButton>();
            edit.normalBgSprite = "ButtonMenu";
            edit.hoveredBgSprite = "ButtonMenuHovered";
            edit.pressedBgSprite = "ButtonMenuPressed";
            edit.focusedBgSprite = "ButtonMenu";
            edit.textScale = 0.8f;
            edit.size = new Vector2(230f, 28f);
            edit.text = Lang.T("map_rel_edit");
            edit.eventClick += (c, p) => Congregations.Toggle();
            return legend;
        }

        internal static void Refresh(UIPanel legend)
        {
            if (legend == null || list == null || Religion.C[0] == null) return;
            var sb = new System.Text.StringBuilder();
            int total = Mathf.Max(1, Religion.Residents);
            for (int g = 0; g < Religion.Slots; g++)
                if (Religion.C[g].Enabled) sb.Append("<color #").Append(Hex(Religion.Colors[g])).Append('>').Append(Religion.C[g].Name).Append("</color>  ").Append(Mathf.RoundToInt(Religion.Members[g + 2] * 100f / total)).Append("%\n");
            sb.Append("<color #").Append(Hex(Religion.InterfaithColor)).Append('>').Append(Lang.T("rel_interfaith")).Append("</color>");
            string text = sb.ToString();
            string sum = Lang.F(Lang.T("rel_total"), Religion.Residents, Mathf.RoundToInt(Religion.CitySat * 100f)) + "\n" + string.Format(Lang.T("rel_nonrel_line"), Lang.T("rel_nonrel"), Mathf.RoundToInt(Religion.Members[1] * 100f / total));
            if (text == shown && totals.text == sum) return;
            shown = text;
            list.text = text;
            totals.text = sum;
            totals.relativePosition = new Vector3(15f, list.relativePosition.y + list.height + 8f);
            edit.relativePosition = new Vector3(15f, totals.relativePosition.y + totals.height + 8f);
            legend.height = edit.relativePosition.y + edit.height + 12f;
        }
    }

    public class Congregations : MonoBehaviour
    {
        static Congregations instance;
        UIPanel panel;
        UILabel stats;
        UIDropDown select, rhythm, day, funeral, giving, inst, conv, leisure, family, edu;
        UICheckBox present;
        UITextField nameField;
        UISlider share, nonRel;
        int sel;
        bool loading;
        float next;

        internal static void Toggle()
        {
            if (instance == null) instance = new GameObject("CityConsequencesCongregations").AddComponent<Congregations>();
            else
            {
                if (instance.panel.isVisible) Ui.ClosePopups(instance.panel);
                instance.panel.isVisible = !instance.panel.isVisible;
            }
            if (instance.panel.isVisible) { instance.panel.BringToFront(); instance.Load(); instance.Stats(); }
        }

        internal static void Remove()
        {
            if (instance != null) Destroy(instance.gameObject);
            instance = null;
        }

        void Awake() => Create();

        void OnDestroy()
        {
            if (panel != null) Destroy(panel.gameObject);
        }

        void Update()
        {
            if (panel == null || !panel.isVisible || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            if (!Religion.On) { Ui.ClosePopups(panel); panel.isVisible = false; return; }
            Stats();
        }

        Congregation Cur => Religion.C[sel];

        void Create()
        {
            UIView view = UIView.GetAView();
            const float w = 520f, h = 640f;
            panel = view.AddUIComponent(typeof(UIPanel)) as UIPanel;
            panel.name = "CityConsequencesCongregationPanel";
            panel.backgroundSprite = "MenuPanel2";
            panel.size = new Vector2(w, h);
            panel.relativePosition = new Vector3((view.fixedWidth - w) / 2f, (view.fixedHeight - h) / 2f);
            UIDragHandle drag = panel.AddUIComponent<UIDragHandle>();
            drag.size = new Vector2(w, 40f);
            drag.relativePosition = Vector3.zero;
            drag.target = panel;
            UILabel title = panel.AddUIComponent<UILabel>();
            title.text = Lang.T("rel_title");
            title.textScale = 1.1f;
            title.relativePosition = new Vector3(14f, 12f);
            title.isInteractive = false;
            UIButton close = panel.AddUIComponent<UIButton>();
            close.normalBgSprite = "buttonclose";
            close.hoveredBgSprite = "buttonclosehover";
            close.pressedBgSprite = "buttonclosepressed";
            close.size = new Vector2(32f, 32f);
            close.relativePosition = new Vector3(w - 38f, 4f);
            close.eventClick += (c, e) => { Ui.ClosePopups(panel); panel.isVisible = false; };
            UIScrollablePanel s = Scroll(panel, 48f);
            stats = Text(s, "", 0.8f);
            Header(s, "rel_newcomers");
            nonRel = Slider(s, "rel_nonrel_share", v => Religion.NonRelShare = (int)v);
            Header(s, "rel_title");
            select = Dropdown(s, "rel_edit", Names(), i => { sel = i; Load(); });
            present = Check(s, "rel_enabled", v => { Cur.Enabled = v; Names(true); });
            nameField = Field(s, "rel_name", v => { if (!string.IsNullOrEmpty(v)) Cur.Name = v; Names(true); });
            share = Slider(s, "rel_share", v => Cur.Share = (int)v);
            Header(s, "rel_practices");
            rhythm = Dropdown(s, "rel_rhythm", Items("r_weekly", "r_daily", "r_fest"), i => Cur.Rhythm = (Rhythm)i);
            day = Dropdown(s, "rel_day", Items("day_0", "day_1", "day_2", "day_3", "day_4", "day_5", "day_6"), i => Cur.Day = (i + 1) % 7);
            funeral = Dropdown(s, "rel_funeral", Items("f_burial", "f_crem", "f_either"), i => Cur.Funeral = (Funeral)i);
            giving = Dropdown(s, "rel_giving", Items("g_tithe", "g_alms", "g_none"), i => Cur.Giving = (Giving)i);
            inst = Dropdown(s, "rel_inst", Items("i_schools", "i_clinics", "g_none"), i => Cur.Institution = (Institution)i);
            conv = Dropdown(s, "rel_conv", Items("c_miss", "c_hered"), i => Cur.Conversion = (Conversion)i);
            leisure = Dropdown(s, "rel_leisure", Items("l_free", "l_austere"), i => Cur.Leisure = (Leisure)i);
            family = Dropdown(s, "rel_family", Items("fa_small", "fa_avg", "fa_large"), i => Cur.Family = (Family)i);
            edu = Dropdown(s, "rel_edu", Items("e_equal", "e_schol", "e_trad", "e_insul"), i => Cur.Education = (EduNorm)i);
        }

        static string[] Items(params string[] keys)
        {
            var r = new string[keys.Length];
            for (int i = 0; i < keys.Length; i++) r[i] = Lang.T(keys[i]);
            return r;
        }

        string[] Names(bool apply = false)
        {
            var n = new string[Religion.Slots];
            for (int i = 0; i < Religion.Slots; i++) n[i] = Religion.C[i].Enabled ? Religion.C[i].Name : Religion.C[i].Name + " (" + Lang.T("rel_absent") + ")";
            if (apply && select != null)
            {
                loading = true;
                select.items = n;
                select.selectedIndex = sel;
                loading = false;
            }
            return n;
        }

        void Load()
        {
            if (Religion.C[0] == null) return;
            loading = true;
            Congregation c = Cur;
            nonRel.value = Religion.NonRelShare;
            present.isChecked = c.Enabled;
            nameField.text = c.Name;
            share.value = c.Share;
            rhythm.selectedIndex = (int)c.Rhythm;
            day.selectedIndex = (c.Day + 6) % 7;
            funeral.selectedIndex = (int)c.Funeral;
            giving.selectedIndex = (int)c.Giving;
            inst.selectedIndex = (int)c.Institution;
            conv.selectedIndex = (int)c.Conversion;
            leisure.selectedIndex = (int)c.Leisure;
            family.selectedIndex = (int)c.Family;
            edu.selectedIndex = (int)c.Education;
            select.items = Names();
            select.selectedIndex = sel;
            loading = false;
        }

        void Stats()
        {
            if (Religion.C[0] == null) return;
            int total = Mathf.Max(1, Religion.Residents);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(Lang.F(Lang.T("rel_total"), Religion.Residents, Mathf.RoundToInt(Religion.CitySat * 100f)));
            sb.AppendLine(string.Format(Lang.T("rel_nonrel_line"), Lang.T("rel_nonrel"), Mathf.RoundToInt(Religion.Members[1] * 100f / total)));
            for (int g = 0; g < Religion.Slots; g++)
            {
                if (!Religion.C[g].Enabled) continue;
                string line = string.Format(Lang.T("rel_line"), Religion.C[g].Name, Mathf.RoundToInt(Religion.Members[g + 2] * 100f / total), Mathf.RoundToInt(Religion.GroupSat[g] * 100f));
                if (Religion.Festival(g)) line += " · " + Lang.T("rel_festival");
                sb.AppendLine(line);
            }
            string text = sb.ToString().TrimEnd();
            if (stats.text != text) stats.text = text;
        }

        static UIScrollablePanel Scroll(UIPanel p, float top)
        {
            UIScrollablePanel s = p.AddUIComponent<UIScrollablePanel>();
            s.relativePosition = new Vector3(12f, top);
            s.size = new Vector2(p.width - 36f, p.height - top - 12f);
            s.autoLayout = true;
            s.autoLayoutDirection = LayoutDirection.Vertical;
            s.autoLayoutPadding = new RectOffset(0, 0, 3, 3);
            s.clipChildren = true;
            s.scrollWheelDirection = UIOrientation.Vertical;
            UIScrollbar sb = p.AddUIComponent<UIScrollbar>();
            sb.width = 10f;
            sb.height = s.height;
            sb.orientation = UIOrientation.Vertical;
            sb.relativePosition = new Vector3(p.width - 20f, top);
            sb.incrementAmount = 40f;
            sb.autoHide = true;
            UISlicedSprite track = sb.AddUIComponent<UISlicedSprite>();
            track.relativePosition = Vector3.zero;
            track.size = sb.size;
            track.fillDirection = UIFillDirection.Vertical;
            track.spriteName = "ScrollbarTrack";
            sb.trackObject = track;
            UISlicedSprite thumb = track.AddUIComponent<UISlicedSprite>();
            thumb.relativePosition = Vector3.zero;
            thumb.fillDirection = UIFillDirection.Vertical;
            thumb.width = track.width;
            thumb.spriteName = "ScrollbarThumb";
            sb.thumbObject = thumb;
            s.verticalScrollbar = sb;
            return s;
        }

        static UILabel Text(UIComponent parent, string text, float scale, float width = 0f)
        {
            UILabel l = parent.AddUIComponent<UILabel>();
            l.textScale = scale;
            l.wordWrap = true;
            l.autoSize = false;
            l.autoHeight = true;
            l.width = width > 0f ? width : parent.width - 4f;
            l.text = text;
            return l;
        }

        static void Header(UIComponent parent, string key)
        {
            UILabel l = Text(parent, Lang.T(key), 1f);
            l.textColor = new Color32(255, 196, 110, 255);
            l.padding = new RectOffset(0, 0, 8, 2);
        }

        static UIPanel Row(UIComponent parent, string key)
        {
            UIPanel r = parent.AddUIComponent<UIPanel>();
            r.size = new Vector2(parent.width - 4f, 30f);
            UILabel l = Text(r, Lang.T(key), 0.8f, 175f);
            l.relativePosition = new Vector3(0f, 7f);
            return r;
        }

        UIDropDown Dropdown(UIComponent parent, string key, string[] items, Action<int> changed)
        {
            UIPanel row = Row(parent, key);
            UIDropDown dd = Ui.Dropdown(row, items, row.width - 182f);
            dd.relativePosition = new Vector3(180f, 1f);
            dd.eventSelectedIndexChanged += (c, i) => { if (!loading) changed(i); };
            return dd;
        }

        UISlider Slider(UIComponent parent, string key, Action<float> changed)
        {
            UIPanel r = Row(parent, key);
            UISlider s = r.AddUIComponent<UISlider>();
            s.size = new Vector2(r.width - 230f, 10f);
            s.relativePosition = new Vector3(180f, 10f);
            s.backgroundSprite = "ScrollbarTrack";
            s.minValue = 0f;
            s.maxValue = 100f;
            s.stepSize = 1f;
            UISlicedSprite t = s.AddUIComponent<UISlicedSprite>();
            t.spriteName = "SliderBudget";
            t.size = new Vector2(10f, 20f);
            s.thumbObject = t;
            UILabel v = Text(r, "", 0.8f, 40f);
            v.relativePosition = new Vector3(r.width - 42f, 7f);
            s.eventValueChanged += (c, val) => { v.text = ((int)val).ToString(); if (!loading) changed(val); };
            return s;
        }

        UICheckBox Check(UIComponent parent, string key, Action<bool> changed)
        {
            UIPanel r = Row(parent, key);
            UICheckBox cb = r.AddUIComponent<UICheckBox>();
            cb.size = new Vector2(20f, 20f);
            cb.relativePosition = new Vector3(180f, 5f);
            UISprite u = cb.AddUIComponent<UISprite>();
            u.spriteName = "check-unchecked";
            u.size = cb.size;
            u.relativePosition = Vector3.zero;
            UISprite k = u.AddUIComponent<UISprite>();
            k.spriteName = "check-checked";
            k.size = cb.size;
            k.relativePosition = Vector3.zero;
            cb.checkedBoxObject = k;
            cb.eventCheckChanged += (c, v) => { if (!loading) changed(v); };
            return cb;
        }

        UITextField Field(UIComponent parent, string key, Action<string> changed)
        {
            UIPanel r = Row(parent, key);
            UITextField f = r.AddUIComponent<UITextField>();
            f.size = new Vector2(r.width - 182f, 26f);
            f.relativePosition = new Vector3(180f, 2f);
            f.normalBgSprite = "TextFieldPanel";
            f.hoveredBgSprite = "TextFieldPanelHovered";
            f.focusedBgSprite = "TextFieldPanel";
            f.selectionSprite = "EmptySprite";
            f.builtinKeyNavigation = true;
            f.isInteractive = true;
            f.readOnly = false;
            f.maxLength = 40;
            f.textScale = 0.85f;
            f.padding = new RectOffset(6, 6, 6, 6);
            f.horizontalAlignment = UIHorizontalAlignment.Left;
            f.textColor = Color.black;
            f.color = Color.white;
            f.eventTextSubmitted += (c, v) => { if (!loading) changed(v); };
            return f;
        }
    }

    public class MapTicker : MonoBehaviour
    {
        void Update()
        {
            try { Maps.Tick(); }
            catch (Exception e) { Debug.LogException(e); enabled = false; }
        }
    }

    static class Badges
    {
        internal const int Door = 0, Rent = 1;

        struct Mark
        {
            internal Vector3 Pos;
            internal bool Severe;
        }

        struct Pop
        {
            internal ushort Id;
            internal int Kind;
            internal Vector3 Pos;
            internal bool Severe;
            internal float Start;
        }

        const float PopTime = 0.5f;
        static Material material;
        static bool tried;
        static readonly Vector4[] sprites = new Vector4[4];
        static readonly object sync = new object();
        static readonly Dictionary<int, Mark> marks = new Dictionary<int, Mark>();
        static readonly byte[] on = new byte[Housing.Buildings];
        static readonly List<Pop> pops = new List<Pop>();
        static readonly Dictionary<ushort, Vector3> exact = new Dictionary<ushort, Vector3>();
        static readonly List<KeyValuePair<int, Mark>> shown = new List<KeyValuePair<int, Mark>>();
        static readonly FastList<NotificationManager.BufferedItem> items = new FastList<NotificationManager.BufferedItem>();
        static readonly Vector4[] regions = new Vector4[16], positions = new Vector4[16], parameters = new Vector4[16];
        static readonly FieldInfo alpha = typeof(NotificationManager).GetField("m_notificationAlpha", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly int idRegions = Shader.PropertyToID("_AtlasRegions"), idPositions = Shader.PropertyToID("_Positions"), idParams = Shader.PropertyToID("_Params");
        static int frame = -1, second;

        static bool Ready(NotificationManager nm)
        {
            if (material != null) return true;
            if (tried || nm.m_notificationMaterial == null) return false;
            tried = true;
            try
            {
                var textures = new Texture2D[4];
                string[] names = { Icons.Minor, Icons.Major, Icons.RentMinor, Icons.RentMajor };
                for (int i = 0; i < 4; i++) if ((textures[i] = Icons.Load(names[i])) == null) return false;
                var sheet = new Texture2D(256, 256, TextureFormat.ARGB32, true);
                Rect[] r = sheet.PackTextures(textures, 4, 512);
                sheet.filterMode = FilterMode.Trilinear;
                sheet.wrapMode = TextureWrapMode.Clamp;
                for (int i = 0; i < 4; i++) sprites[i] = new Vector4(r[i].xMin, r[i].yMin, r[i].xMax, r[i].yMax);
                material = new Material(nm.m_notificationMaterial) { mainTexture = sheet };
            }
            catch (Exception e) { Debug.LogException(e); }
            return material != null;
        }

        internal static void Clear()
        {
            lock (sync)
            {
                marks.Clear();
                pops.Clear();
            }
            Array.Clear(on, 0, on.Length);
            exact.Clear();
            items.Clear();
            if (material != null) UnityEngine.Object.Destroy(material);
            material = null;
            tried = false;
        }

        static Vector3 Top(ref Building b)
        {
            b.GetTotalPosition(out Vector3 position, out Quaternion _, out Vector3 size);
            position.y += size.y;
            return position;
        }

        static bool Hidden(ref Building b) => (b.m_flags & (Building.Flags.Abandoned | Building.Flags.Collapsed | Building.Flags.BurnedDown | Building.Flags.Deleted)) != 0 || b.m_problems.IsFatal;

        static int Enabled => (Settings.Homeless.value ? 1 << Door : 0) | (Settings.Rent.value ? 1 << Rent : 0);

        internal static void Update(ushort id, ref Building b, int old, int level) => Set(Door, id, ref b, old >= Housing.DoorMinor, level >= Housing.DoorMinor, old >= Housing.DoorMajor, level >= Housing.DoorMajor);

        internal static void Set(int kind, ushort id, ref Building b, bool was, bool now, bool wasSevere, bool severe)
        {
            if (!was && !now) return;
            int key = id | kind << 16;
            lock (sync)
            {
                if (now)
                {
                    on[id] |= (byte)(1 << kind);
                    marks[key] = new Mark { Pos = Top(ref b), Severe = severe };
                }
                else
                {
                    on[id] &= (byte)~(1 << kind);
                    marks.Remove(key);
                    if (!Hidden(ref b)) pops.Add(new Pop { Id = id, Kind = kind, Pos = Top(ref b), Severe = wasSevere, Start = -1f });
                }
            }
        }

        internal static void Forget(ushort id)
        {
            if (on[id] == 0) return;
            on[id] = 0;
            lock (sync)
            {
                marks.Remove(id | Door << 16);
                marks.Remove(id | Rent << 16);
            }
        }

        static int Phase(ref Building b, NotificationManager nm, int mask, out int count)
        {
            if (Time.frameCount != frame)
            {
                frame = Time.frameCount;
                second = (int)Time.realtimeSinceStartup;
            }
            count = Notification.RemoveProblems(b.m_problems, nm.NotificationsHidden).Count;
            return second % (count + (mask & 1) + (mask >> 1 & 1));
        }

        internal static void Exact(ref Building b, ushort id, BuildingInfo info, ref RenderManager.Instance data, int before)
        {
            int mask = on[id] & Enabled;
            if (mask == 0 || !Sim.Active) return;
            Vector3 position;
            if (b.m_subBuilding != 0 && info.m_placementMode == BuildingInfo.PlacementMode.Roadside)
            {
                b.GetTotalPosition(out position, out Quaternion _, out Vector3 size);
                position.y += Mathf.Min(size.y, data.m_dataVector0.y);
            }
            else
            {
                position = data.m_position;
                position.y += Mathf.Min(info.m_size.y, data.m_dataVector0.y);
            }
            exact[id] = position;
            NotificationManager nm = Singleton<NotificationManager>.instance;
            if (material != null && !Hidden(ref b) && Phase(ref b, nm, mask, out int count) >= count && nm.m_bufferedItems.m_size > before) nm.m_bufferedItems.m_size = before;
        }

        static void Add(RenderManager.CameraInfo cameraInfo, Vector3 position, Vector4 p, float scale, int sprite)
        {
            items.Add(new NotificationManager.BufferedItem
            {
                m_position = new Vector4(position.x, position.y, position.z, scale),
                m_params = p,
                m_distanceSqr = Vector3.SqrMagnitude(position - cameraInfo.m_position),
                m_regionIndex = sprite
            });
        }

        internal static void Draw(RenderManager.CameraInfo cameraInfo)
        {
            int enabled = Enabled;
            if (!Sim.Active || enabled == 0 || marks.Count == 0 && pops.Count == 0) return;
            NotificationManager nm = Singleton<NotificationManager>.instance;
            if (!Ready(nm)) return;
            ToolController tools = Singleton<ToolManager>.instance.m_properties;
            if (tools != null && tools.m_mode == ItemClass.Availability.AssetEditor) return;
            float now = Time.realtimeSinceStartup;
            shown.Clear();
            lock (sync)
            {
                foreach (var kv in marks) shown.Add(kv);
                for (int i = pops.Count - 1; i >= 0; i--)
                {
                    Pop pop = pops[i];
                    if (pop.Start < 0f) { pop.Start = now; pops[i] = pop; }
                    float t = now - pop.Start;
                    if (t >= PopTime) { pops.RemoveAt(i); continue; }
                    if ((enabled & 1 << pop.Kind) == 0) continue;
                    Vector3 at = exact.TryGetValue(pop.Id, out Vector3 e) ? e : pop.Pos;
                    at.y += t * 5f;
                    Add(cameraInfo, at, new Vector4(0f, 1f - t * 2f, 0f, 0f), 1f + t * 2f, pop.Kind * 2 + (pop.Severe ? 1 : 0));
                }
            }
            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            foreach (var kv in shown)
            {
                ushort id = (ushort)(kv.Key & 0xFFFF);
                int kind = kv.Key >> 16, mask = on[id] & enabled;
                if ((mask & 1 << kind) == 0 || Hidden(ref buildings[id])) continue;
                if (Phase(ref buildings[id], nm, mask, out int count) != count + (kind == Rent ? mask & 1 : 0)) continue;
                Vector3 position = exact.TryGetValue(id, out Vector3 x) ? x : kv.Value.Pos;
                if (!cameraInfo.Intersect(position, 10f)) continue;
                bool severe = kv.Value.Severe;
                var p = new Vector4(severe ? 0.2f : 0.1f, 1f, severe ? 6f : 5f, 0f);
                if (nm.FadeAwayNotifications)
                {
                    float d = Vector3.SqrMagnitude(cameraInfo.m_position - position) * 1E-06f;
                    p.y = Mathf.Max(0f, 1f - d * d);
                    if (p.y <= 0f) continue;
                }
                Add(cameraInfo, position, p, 1f, kind * 2 + (severe ? 1 : 0));
            }
            if (exact.Count > 2 * shown.Count + 64) exact.Clear();
            if (items.m_size == 0) return;
            if (items.m_size > 1) Array.Sort(items.m_buffer, 0, items.m_size, Farther.Instance);
            float fade = alpha != null ? (float)alpha.GetValue(nm) : 1f;
            if (fade >= 0.001f && nm.m_notificationMesh != null)
            {
                int done = 0;
                while (done < items.m_size)
                {
                    int n = Mathf.Min(16, items.m_size - done);
                    for (int i = 0; i < n; i++)
                    {
                        NotificationManager.BufferedItem item = items.m_buffer[done++];
                        Vector4 p = item.m_params;
                        p.y *= fade;
                        regions[i] = sprites[item.m_regionIndex & 3];
                        positions[i] = item.m_position;
                        parameters[i] = p;
                    }
                    for (int i = n; i < 16; i++)
                    {
                        regions[i] = Vector4.zero;
                        positions[i] = cameraInfo.m_forward * -100000f;
                        parameters[i] = Vector4.zero;
                    }
                    material.SetVectorArray(idRegions, regions);
                    material.SetVectorArray(idPositions, positions);
                    material.SetVectorArray(idParams, parameters);
                    if (material.SetPass(0)) Graphics.DrawMeshNow(nm.m_notificationMesh, Matrix4x4.identity);
                }
            }
            items.Clear();
        }

        sealed class Farther : IComparer<NotificationManager.BufferedItem>
        {
            internal static readonly Farther Instance = new Farther();
            public int Compare(NotificationManager.BufferedItem a, NotificationManager.BufferedItem b) => b.m_distanceSqr.CompareTo(a.m_distanceSqr);
        }
    }

    static class Icons
    {
        internal static Texture2D Load(string n)
        {
            using (System.IO.Stream stream = typeof(Icons).Assembly.GetManifestResourceStream("CityConsequences." + n + ".png"))
            {
                if (stream == null) { Debug.LogWarning("[City Consequences] Missing icon " + n); return null; }
                var bytes = new byte[stream.Length];
                int read = 0;
                while (read < bytes.Length) { int k = stream.Read(bytes, read, bytes.Length - read); if (k <= 0) break; read += k; }
                var t = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                t.LoadImage(bytes);
                t.name = n;
                return t;
            }
        }

        internal const string AntiHomeless = "IconPolicyAntiHomeless", Homeless = "IconPolicyBindle", Minor = "BuildingNotificationHomelessFirst", Major = "BuildingNotificationHomeless", RentMinor = "BuildingNotificationRentFirst", RentMajor = "BuildingNotificationRent", Rent = "InfoIconRent", Religion = "InfoIconReligion", HomelessInfo = "InfoIconHomeless", PublicHousing = "IconPolicyPublicHousing", NoShortStay = "IconPolicyNoShortStay", WorshipTax = "IconPolicyWorshipTax", Outreach = "IconPolicyOutreach", InterfaithPolicy = "IconPolicyInterfaith";
        static readonly string[] States = { "", "Disabled", "Focused", "Hovered", "Pressed" };
        static UITextureAtlas atlas;
        static bool tried;

        internal static UITextureAtlas Atlas
        {
            get
            {
                if (atlas != null || tried) return atlas;
                tried = true;
                try { atlas = Build(); }
                catch (Exception e) { Debug.LogException(e); }
                return atlas;
            }
        }

        static UITextureAtlas Build()
        {
            var names = new List<string>();
            var textures = new List<Texture2D>();
            var all = new List<string>();
            foreach (string baseName in new[] { AntiHomeless, Homeless, Rent, Religion, HomelessInfo, PublicHousing, NoShortStay, WorshipTax, Outreach, InterfaithPolicy })
                foreach (string state in States) all.Add(baseName + state);
            all.Add(Minor);
            all.Add(Major);
            all.Add(RentMinor);
            all.Add(RentMajor);
            foreach (string n in all)
            {
                Texture2D t = Load(n);
                if (t == null) continue;
                names.Add(t.name);
                textures.Add(t);
            }
            if (textures.Count == 0) return null;
            var sheet = new Texture2D(256, 256, TextureFormat.ARGB32, false);
            Rect[] rects = sheet.PackTextures(textures.ToArray(), 2, 1024);
            sheet.filterMode = FilterMode.Bilinear;
            UITextureAtlas basis = UIView.GetAView() != null ? UIView.GetAView().defaultAtlas : null;
            var result = ScriptableObject.CreateInstance<UITextureAtlas>();
            result.name = "CityConsequencesIcons";
            result.material = basis != null ? UnityEngine.Object.Instantiate(basis.material) : new Material(Shader.Find("UI/Default UI Shader"));
            result.material.mainTexture = sheet;
            for (int i = 0; i < textures.Count; i++)
                result.AddSprite(new UITextureAtlas.SpriteInfo { name = names[i], texture = textures[i], region = rects[i] });
            return result;
        }
    }

    public class PolicyToggle : MonoBehaviour
    {
        PoliciesPanel panel;
        UICheckBox check;
        UIButton button;
        UISprite overlay;
        string sprite, disabled;
        byte bit;

        internal void Init(PoliciesPanel p, UICheckBox c, UIButton b, byte policy, UISprite o, string s)
        {
            panel = p;
            check = c;
            button = b;
            bit = policy;
            overlay = o;
            sprite = s;
            disabled = s + "Disabled";
            if (check != null) check.eventCheckChanged += Changed;
        }

        void Changed(UIComponent c, bool value)
        {
            byte district = panel.targetDistrict;
            if (Housing.HasPolicy(district, bit) == value) return;
            byte b = bit;
            Singleton<SimulationManager>.instance.AddAction(() => Housing.SetPolicy(district, b, value));
        }

        void Update()
        {
            if (check == null || panel == null || !check.isVisible) return;
            bool on = Housing.HasPolicy(panel.targetDistrict, bit);
            if (check.isChecked != on) check.isChecked = on;
            if (button != null) button.state = on ? UIButton.ButtonState.Focused : UIButton.ButtonState.Normal;
            if (overlay == null || button == null) return;
            string name = button.isEnabled ? sprite : disabled;
            if (!ReferenceEquals(overlay.spriteName, name)) overlay.spriteName = name;
        }
    }

    static class Patches
    {
        internal const string Id = "com.cityconsequences";

        internal static void Apply()
        {
            var h = new Harmony(Id);
            Patch(h, typeof(BuildingManager), "ReleaseBuilding", new[] { typeof(ushort) }, nameof(BuildingReleased), null);
            Patch(h, typeof(CitizenManager), "ReleaseCitizen", new[] { typeof(uint) }, nameof(KeepHomeless), null);
            Patch(h, typeof(DisasterHelpers), "RemovePeople", null, nameof(RemoveStart), nameof(RemoveEnd));
            Patch(h, typeof(ResidentAI), "UpdateHome", null, nameof(SkipHomeSearch), null);
            Patch(h, typeof(HumanAI), "FindVisitPlace", null, nameof(VisitPlace), null);
            Patch(h, typeof(ResidentAI), "SimulationStep", new[] { typeof(uint), typeof(CitizenUnit).MakeByRefType() }, nameof(Goods), null);
            Patch(h, typeof(ResidentAI), "UpdateWellbeing", null, nameof(WellbeingBefore), nameof(Wellbeing));
            Patch(h, typeof(ResidentAI), "UpdateWorkplace", null, nameof(JobSearch), nameof(JobSearched));
            Patch(h, typeof(PoliceCarAI), "ArrestCriminals", null, null, nameof(Arrested));
            Patch(h, typeof(PoliceCarAI), "ArriveAtTarget", null, nameof(Raid), null);
            Patch(h, typeof(ResidentAI), "StartTransfer", null, nameof(MoveWithinCity), null);
            Patch(h, typeof(ResidentAI), "GetVehicleInfo", null, null, nameof(VehicleChoice));
            Patch(h, typeof(CitizenAI), "StartPathFind", new[] { typeof(ushort), typeof(CitizenInstance).MakeByRefType(), typeof(Vector3), typeof(Vector3), typeof(VehicleInfo), typeof(bool), typeof(bool) }, nameof(PathRules), null, Priority.First);
            Patch(h, typeof(OutsideConnectionAI), "StartConnectionTransferImpl", null, nameof(Immigrate), nameof(Immigrated));
            Patch(h, typeof(ResidentialBuildingAI), "SimulationStepActive", null, nameof(ResidentialStart), nameof(ResidentialEnd));
            Patch(h, typeof(PrivateBuildingAI), "SimulationStepActive", null, nameof(Problems), null);
            Patch(h, typeof(ZoneBlock), "SimulationStep", new[] { typeof(ushort) }, nameof(Growth), null);
            foreach (Type t in new[] { typeof(CommercialBuildingAI), typeof(OfficeBuildingAI), typeof(IndustrialBuildingAI), typeof(IndustrialExtractorAI) })
                Patch(h, t, "SimulationStepActive", null, null, nameof(Workplace));
            Patch(h, typeof(ParkAI), "ProduceGoods", null, nameof(ParkTents), null);
            Patch(h, typeof(PropInstance), "CheckOverlap", null, nameof(TentOverlap), null);
            Patch(h, typeof(Building), "RenderInstance", new[] { typeof(RenderManager.CameraInfo), typeof(ushort), typeof(int), typeof(BuildingInfo), typeof(RenderManager.Instance).MakeByRefType() }, nameof(BadgeBefore), nameof(BadgeAfter));
            Patch(h, typeof(NotificationManager), "EndOverlayImpl", null, null, nameof(BadgeDraw));
            Patch(h, typeof(BuildingWorldInfoPanel), "UpdateBindings", null, null, nameof(PanelProblems));
            Patch(h, typeof(GameInfoViewsPanel), "RefreshPanel", null, nameof(MenuBefore), nameof(MenuAfter));
            Patch(h, typeof(GameInfoViewsPanel), "ShowSelectedIndex", null, nameof(MenuSelected), null);
            Patch(h, typeof(GameInfoViewsPanel), "OnButtonClicked", null, nameof(MenuClicked), null);
            MethodInfo colorMap = AccessTools.DeclaredMethod(typeof(BuildingManager), "UpdateColorMap");
            if (colorMap != null) h.Patch(colorMap, transpiler: new HarmonyMethod(typeof(Patches), nameof(MapColors)));
            else Debug.LogError("[City Consequences] Missing BuildingManager.UpdateColorMap");
            Patch(h, typeof(BuildingManager), "CreateBuilding", null, null, nameof(BuildingCreated));
            Patch(h, typeof(NetManager), "CreateSegment", new[] { typeof(ushort).MakeByRefType(), typeof(Randomizer).MakeByRefType(), typeof(NetInfo), typeof(TreeInfo), typeof(ushort), typeof(ushort), typeof(Vector3), typeof(Vector3), typeof(uint), typeof(uint), typeof(bool) }, null, nameof(SegmentCreated));
            Patch(h, typeof(RoadBaseAI), "GetMaintenanceCost", new[] { typeof(Vector3), typeof(Vector3) }, null, nameof(Upkeep));
            Patch(h, typeof(PlayerNetAI), "GetMaintenanceCost", Type.EmptyTypes, null, nameof(UpkeepPreview));
            Patch(h, typeof(PlayerNetAI), "GetMaintenanceCost", new[] { typeof(Vector3), typeof(Vector3) }, null, nameof(WornUpkeep));
            Patch(h, typeof(Patches), typeof(PlayerNetAI), "SimulationStep", new[] { typeof(ushort), typeof(NetSegment).MakeByRefType() }, nameof(SegmentStep), null, Priority.First, nameof(SegmentStepDone));
            Patch(h, typeof(PlayerBuildingAI), "GetMaintenanceCost", Type.EmptyTypes, null, nameof(Wages));
            Patch(h, typeof(PoliciesPanel), "RefreshPanel", null, nameof(PoliciesBefore), nameof(PoliciesAfter));
            Patch(h, typeof(ZonedBuildingWorldInfoPanel), "OnSetTarget", null, null, nameof(ZonedPanel));
            Patch(h, typeof(CityServiceWorldInfoPanel), "OnSetTarget", null, null, nameof(ServicePanel));
            MethodInfo workplace = AccessTools.DeclaredMethod(typeof(ResidentAI), "UpdateWorkplace");
            if (workplace != null) h.Patch(workplace, transpiler: new HarmonyMethod(typeof(Patches), nameof(StudentOffers)));
            Religion.Standalone = StandaloneFaith();
            if (Religion.Standalone) { Debug.LogWarning("[City Consequences] Places of Worship is enabled separately, so the built-in religion features stay off"); return; }
            Patch(h, typeof(BuildingInfo), "InitializePrefab", Type.EmptyTypes, nameof(WorshipPrefab), null);
            Patch(h, typeof(ResidentAI), "SimulationStep", new[] { typeof(uint), typeof(CitizenUnit).MakeByRefType() }, nameof(BirthsBefore), nameof(BirthsAfter));
            Patch(h, typeof(ResidentAI), "CanMakeBabies", null, null, nameof(NoBabies));
            Patch(h, typeof(ResidentAI), "FinishSchoolOrWork", null, nameof(SchoolingBefore), nameof(Graduated));
            Patch(h, typeof(ResidentAI), "UpdateLocation", null, nameof(Locating), nameof(Located));
            Patch(h, typeof(ResidentAI), "AttemptAutodidact", null, nameof(SchoolingBefore), nameof(SelfTaught));
            Patch(h, typeof(ResidentAI), "UpdateHealth", null, null, nameof(Healed));
            Patch(h, typeof(CemeteryAI), "StartTransfer", null, nameof(FuneralDispatch), null);
            Patch(h, typeof(HearseAI), "LoadDeadCitizens", null, nameof(HoldBodies), nameof(ReleaseBodies));
        }

        static bool StandaloneFaith()
        {
            try
            {
                foreach (ColossalFramework.Plugins.PluginManager.PluginInfo p in ColossalFramework.Plugins.PluginManager.instance.GetPluginsInfo())
                    if (p != null && p.isEnabled && p.userModInstance != null && p.userModInstance.GetType().FullName == "PlacesOfWorship.Mod") return true;
            }
            catch (Exception e) { Debug.LogException(e); }
            return false;
        }

        static void WorshipPrefab(BuildingInfo __instance)
        {
            try { Religion.Convert(__instance); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static void BirthsBefore(ref CitizenUnit data, out float __state)
        {
            Religion.NoBaby = false;
            __state = 1f;
            if (Sim.Active) __state = Religion.Births(ref data);
        }

        static void BirthsAfter(ResidentAI __instance, uint homeID, ref CitizenUnit data, float __state)
        {
            Religion.NoBaby = false;
            if (__state > 1f && Sim.Active) Religion.ExtraBaby(__instance, homeID, ref data, __state);
        }

        static void NoBabies(ref bool __result)
        {
            if (Religion.NoBaby) __result = false;
        }

        static void SchoolingBefore(ref Citizen data, out Citizen.Flags __state) => __state = Religion.Education(ref data);

        static void Graduated(uint citizenID, ref Citizen data, Citizen.Flags __state) => Religion.Guard(citizenID, ref data, __state);

        static void Locating(uint citizenID) => Religion.Locating = citizenID;

        static void Located() => Religion.Locating = 0;

        static void SelfTaught(ref Citizen data, Citizen.Flags __state) => Religion.Guard(Religion.Locating, ref data, __state);

        static void Healed(uint citizenID, ref Citizen data, bool __result)
        {
            if (!__result && Religion.On) Religion.Health(citizenID, ref data);
        }

        static bool VisitPlace(HumanAI __instance, uint citizenID, ushort sourceBuilding, TransferManager.TransferReason reason)
        {
            if (!Sim.Active || sourceBuilding == 0 || !(__instance is ResidentAI)) return true;
            if (Religion.On && !Religion.Visit(__instance, citizenID, sourceBuilding, reason)) return false;
            return Housing.Outing(citizenID, reason);
        }

        static void Goods(uint homeID, ref CitizenUnit data)
        {
            if (Sim.Active) Housing.Goods(homeID, ref data);
        }

        static bool FuneralDispatch(CemeteryAI __instance, TransferManager.TransferReason material, TransferManager.TransferOffer offer) => material != TransferManager.TransferReason.Dead || !Religion.On || Religion.Dispatch(__instance, offer.Building);

        static void HoldBodies(ref Vehicle data, ushort buildingID, out int __state) => __state = Religion.On ? Religion.HoldBodies(ref data, buildingID) : -1;

        static void ReleaseBodies(int __state)
        {
            if (__state >= 0) Religion.ReleaseBodies(__state);
        }

        static IEnumerable<CodeInstruction> StudentOffers(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo target = AccessTools.Method(typeof(TransferManager), "AddOutgoingOffer");
            MethodInfo replacement = AccessTools.Method(typeof(Housing), nameof(Housing.Offer));
            int swapped = 0;
            foreach (CodeInstruction i in instructions)
            {
                if ((i.opcode == System.Reflection.Emit.OpCodes.Callvirt || i.opcode == System.Reflection.Emit.OpCodes.Call) && i.operand as MethodInfo == target)
                {
                    i.opcode = System.Reflection.Emit.OpCodes.Call;
                    i.operand = replacement;
                    swapped++;
                }
                yield return i;
            }
            if (swapped == 0) Debug.LogWarning("[City Consequences] Could not hook job and school offers");
        }

        internal static void Remove()
        {
            new Harmony(Id).UnpatchAll(Id);
            Politics.Reset();
        }

        static void Patch(Harmony h, Type type, string method, Type[] args, string prefix, string postfix, int priority = Priority.First) =>
            Patch(h, typeof(Patches), type, method, args, prefix, postfix, priority);

        internal static bool Patch(Harmony h, Type owner, Type type, string method, Type[] args, string prefix, string postfix, int priority = Priority.First, string finalizer = null)
        {
            var original = type == null ? null : AccessTools.DeclaredMethod(type, method, args);
            if (original == null)
            {
                Debug.LogError("[City Consequences] Missing " + (type != null ? type.Name : "?") + "." + method);
                return false;
            }
            h.Patch(original,
                prefix == null ? null : new HarmonyMethod(owner, prefix) { priority = priority },
                postfix == null ? null : new HarmonyMethod(owner, postfix),
                null,
                finalizer == null ? null : new HarmonyMethod(owner, finalizer));
            return true;
        }

        static void BuildingReleased(ushort building)
        {
            if (Sim.Active) Housing.Released(building);
        }

        static bool KeepHomeless(uint citizen)
        {
            if (Sim.Active && Settings.Homeless.value && Housing.Keep(citizen)) return false;
            Records.Forget(citizen);
            Religion.Forget(citizen);
            return true;
        }

        static void JobSearch(uint citizenID, ref Citizen data, out int __state)
        {
            __state = -1;
            Housing.Hustle = 0;
            if (!Sim.Active || data.m_workBuilding != 0 || data.m_homeBuilding == 0) return;
            if (Records.Blocked(citizenID))
            {
                if (data.Unemployed == 0) return;
                __state = data.Unemployed;
                data.Unemployed = 0;
                if (Sim.Roll(300) == 0) Chirps.Send("Record", 0);
                return;
            }
            Citizen.AgeGroup age = Citizen.GetAgeGroup(data.Age);
            if (age == Citizen.AgeGroup.Child || age == Citizen.AgeGroup.Senior) return;
            int h = Housing.Hustle = Housing.Hunt(citizenID, ref data);
            if ((h & 1) != 0 && data.Unemployed == 0 && age == Citizen.AgeGroup.Teen)
            {
                __state = 0;
                data.Unemployed = 1;
            }
        }

        static void JobSearched(ref Citizen data, int __state)
        {
            Housing.Hustle = 0;
            if (__state >= 0) data.Unemployed = __state;
        }

        static void Raid(ref Vehicle data)
        {
            if (!Sim.Active || !Settings.Homeless.value || (data.m_flags & Vehicle.Flags.Emergency2) != 0) return;
            ushort target = data.m_targetBuilding;
            if (Housing.Raid(target) && PoliceCarAI.CountCriminals(target) != 0) data.m_flags |= Vehicle.Flags.Emergency2;
        }

        static void Arrested(ref Vehicle vehicleData)
        {
            if (!Sim.Active) return;
            CitizenManager cm = Singleton<CitizenManager>.instance;
            Citizen[] z = cm.m_citizens.m_buffer;
            uint unit = vehicleData.m_citizenUnits;
            bool homeless = false;
            for (int guard = 0; unit != 0 && guard < 524288; guard++)
            {
                for (int i = 0; i < 5; i++)
                {
                    uint c = cm.m_units.m_buffer[unit].GetCitizen(i);
                    if (c == 0 || !z[c].Arrested) continue;
                    Records.Arrest(c, ref z[c]);
                    homeless |= Housing.Tracked(c);
                }
                unit = cm.m_units.m_buffer[unit].m_nextUnit;
            }
            if (homeless) Chirps.Send("Sweep", 0);
        }

        static void RemoveStart(ushort buildingID)
        {
            if (Sim.Active) Housing.InRemove = Housing.Removing(buildingID);
        }

        static void RemoveEnd() => Housing.InRemove = false;

        static bool SkipHomeSearch(uint citizenID) => !(Sim.Active && Housing.Tracked(citizenID));

        static void WellbeingBefore(ref Citizen data, out Citizen.Flags __state) => __state = Religion.Education(ref data);

        static void Wellbeing(uint citizenID, ref Citizen data, Citizen.Flags __state)
        {
            if (!Sim.Active) return;
            if (Religion.On) Religion.Wellbeing(citizenID, ref data, __state);
            Housing.Pension(citizenID, ref data);
            Housing.Tier(citizenID, ref data);
            Records.Tick(citizenID, ref data);
            Housing.DropOut(citizenID, ref data);
            if (!Housing.HomelessTick(citizenID, ref data) && data.m_homeBuilding != 0) Housing.Neighbourhood(citizenID, ref data);
        }

        static bool IsHomeReason(TransferManager.TransferReason reason, out bool family, out int education)
        {
            family = false;
            education = 0;
            switch (reason)
            {
                case TransferManager.TransferReason.Single0: case TransferManager.TransferReason.Single0B: return true;
                case TransferManager.TransferReason.Single1: case TransferManager.TransferReason.Single1B: education = 1; return true;
                case TransferManager.TransferReason.Single2: case TransferManager.TransferReason.Single2B: education = 2; return true;
                case TransferManager.TransferReason.Single3: case TransferManager.TransferReason.Single3B: education = 3; return true;
                case TransferManager.TransferReason.Family0: family = true; return true;
                case TransferManager.TransferReason.Family1: family = true; education = 1; return true;
                case TransferManager.TransferReason.Family2: family = true; education = 2; return true;
                case TransferManager.TransferReason.Family3: family = true; education = 3; return true;
            }
            return false;
        }

        static bool MoveWithinCity(uint citizenID, ref Citizen data, TransferManager.TransferReason reason, TransferManager.TransferOffer offer)
        {
            if (Religion.On && !Religion.Allowed(citizenID, reason, offer.Building)) return false;
            if (Sim.Active && reason >= TransferManager.TransferReason.Worker0 && reason <= TransferManager.TransferReason.Worker3 && data.m_workBuilding == 0 && Records.Blocked(citizenID)) return false;
            if (!Sim.Active || !IsHomeReason(reason, out bool family, out int _)) return true;
            ushort target = offer.Building;
            if (Housing.Tracked(citizenID) || (target != 0 && Housing.MarkedPublic(target))) return false;
            if (!Settings.Rent.value || target == 0 || !Housing.IsHome(target)) return true;
            if (!Housing.WithinBudget(citizenID, ref data, target)) return false;
            int income = family ? Housing.HouseholdIncome(citizenID, ref data) : Housing.CitizenIncome(citizenID), t = family ? Housing.HouseholdTier(citizenID, ref data) : Housing.Tier(citizenID);
            return Housing.Choose(income, t, target) && Housing.Settle(citizenID, ref data, target, family, income, t);
        }

        static bool Immigrate(TransferManager.TransferReason material, TransferManager.TransferOffer offer, ref bool __result)
        {
            if (!Sim.Active || !IsHomeReason(material, out bool family, out int education)) return true;
            ushort target = offer.Building;
            if (target == 0 || !Housing.IsHome(target)) return true;
            bool allowed = Housing.MarkedPublic(target) ? education <= 1 : !Settings.Rent.value || Housing.AdmitImmigrant(target, education, family);
            if (allowed) return true;
            if (!Housing.MarkedPublic(target) && !Housing.Picky) Chirps.Send("Unaffordable", 0);
            __result = false;
            return false;
        }

        static void Immigrated()
        {
            if (Sim.Active) Housing.ImmigrantArrived();
        }

        static void Workplace(ushort buildingID, ref Building buildingData)
        {
            if (!Sim.Active) return;
            if (Settings.Tents.value) Housing.TentHappiness(buildingID, ref buildingData);
            if (Settings.Homeless.value) Housing.Doorstep(buildingID, ref buildingData);
        }

        static void BadgeBefore(out int __state) => __state = Singleton<NotificationManager>.instance.m_bufferedItems.m_size;

        static void BadgeAfter(ref Building __instance, ushort buildingID, BuildingInfo info, ref RenderManager.Instance data, int __state)
        {
            Badges.Exact(ref __instance, buildingID, info, ref data, __state);
        }

        static void BadgeDraw(RenderManager.CameraInfo cameraInfo) => Badges.Draw(cameraInfo);

        static void MenuBefore(GameInfoViewsPanel __instance) => Guard(() => Maps.Before(__instance));
        static void MenuAfter(GameInfoViewsPanel __instance) => Guard(() => Maps.After(__instance));
        static bool MenuSelected(GameInfoViewsPanel __instance) => Maps.Selected(__instance);
        static void MenuClicked() => Maps.Stop();

        static IEnumerable<CodeInstruction> MapColors(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo target = AccessTools.Method(typeof(BuildingAI), "GetColor", new[] { typeof(ushort), typeof(Building).MakeByRefType(), typeof(InfoManager.InfoMode), typeof(InfoManager.SubInfoMode) });
            MethodInfo replacement = AccessTools.Method(typeof(Maps), nameof(Maps.Paint));
            int swapped = 0;
            foreach (CodeInstruction i in instructions)
            {
                if ((i.opcode == System.Reflection.Emit.OpCodes.Callvirt || i.opcode == System.Reflection.Emit.OpCodes.Call) && i.operand as MethodInfo == target)
                {
                    i.opcode = System.Reflection.Emit.OpCodes.Call;
                    i.operand = replacement;
                    swapped++;
                }
                yield return i;
            }
            if (swapped == 0) Debug.LogError("[City Consequences] Info maps could not hook building colors");
        }

        static void PanelProblems(BuildingWorldInfoPanel __instance)
        {
            try { Ui.Problems(__instance); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static bool TentOverlap(ushort propID, ref PropInstance __instance)
        {
            if (!Tents.Mine(propID)) return true;
            __instance.Blocked = false;
            return false;
        }

        static void ParkTents(ushort buildingID, ref int productionRate)
        {
            if (Sim.Active && Settings.Tents.value) productionRate = Housing.ParkOutput(buildingID, productionRate);
        }

        static readonly Dictionary<BuildingInfo, float> unwanted = new Dictionary<BuildingInfo, float>();

        static float Unwanted(BuildingInfo info)
        {
            if (unwanted.TryGetValue(info, out float w)) return w;
            if (unwanted.Count > 4096) unwanted.Clear();
            return unwanted[info] = Weigh(info);
        }

        static int Field(object ai, string name)
        {
            FieldInfo f = ai.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
            return f != null && f.FieldType == typeof(int) ? (int)f.GetValue(ai) : 0;
        }

        const float TourismFull = 25f, TourismRelief = 0.3f;

        static float Weigh(BuildingInfo info)
        {
            float w = Nuisance(info);
            if (w <= 0f) return 0f;
            float draw = info.m_buildingAI is HotelAI ? TourismFull * 0.5f : Field(info.m_buildingAI, "m_attractivenessAccumulation");
            return w * (1f - TourismRelief * Mathf.Clamp01(draw / TourismFull));
        }

        static float Nuisance(BuildingInfo info)
        {
            BuildingAI ai = info.m_buildingAI;
            ItemClass c = info.m_class;
            if (c.m_service == ItemClass.Service.Garbage) return 1f;
            if (c.m_subService == ItemClass.SubService.PublicTransportPlane || ai is AirportBuildingAI) return 0.9f;
            if (ai is PoliceStationAI && c.m_level >= ItemClass.Level.Level4) return 0.8f;
            int noise = Field(ai, "m_noiseAccumulation"), pollution = Field(ai, "m_pollutionAccumulation");
            if (ai is PowerPlantAI && (noise > 0 || pollution > 0)) return 0.8f;
            if (ai is CargoStationAI) return 0.7f;
            if (ai is WaterFacilityAI && Field(ai, "m_sewageOutlet") > 0) return 0.6f;
            if (c.m_service == ItemClass.Service.PlayerIndustry) return 0.6f;
            if (ai is CemeteryAI) return 0.5f;
            if (ai is DepotAI) return 0.3f;
            if (noise <= 0 && pollution <= 0) return 0f;
            return Mathf.Clamp(0.15f + noise / 200f + pollution / 150f, 0.15f, 0.7f);
        }

        static ToolBase Tool()
        {
            ToolController c = ToolsModifierControl.toolController;
            return c != null ? c.CurrentTool : null;
        }

        static void BuildingCreated(bool __result, BuildingInfo info, Vector3 position)
        {
            if (!__result || !Sim.Active || info == null || info.m_buildingAI == null || !(Tool() is BuildingTool)) return;
            float weight = Unwanted(info);
            if (weight > 0f) Community.Objection(position, info.m_buildingAI.GetConstructionCost(), weight, info.m_class);
        }

        static void SegmentCreated(bool __result, NetInfo info, ushort startNode, ushort endNode)
        {
            if (!__result || !Sim.Active || info == null || info.m_netAI == null || !(Tool() is NetTool)) return;
            NetAI ai = info.m_netAI;
            float weight = ai is RunwayAI ? 0.9f : ai is TrainTrackBaseAI && !(ai is TrainTrackTunnelAI) ? 0.6f : ai is MetroTrackBridgeAI ? 0.4f : ai is RoadBaseAI ? (ai.IsHighway() ? 1f : info.m_halfWidth >= 15f ? 0.5f : 0f) : 0f;
            if (weight <= 0f) return;
            NetNode[] n = Singleton<NetManager>.instance.m_nodes.m_buffer;
            Vector3 a = n[startNode].m_position, b = n[endNode].m_position;
            Community.Objection((a + b) * 0.5f, info.m_netAI.GetConstructionCost(a, b, n[startNode].m_elevation, n[endNode].m_elevation), weight, info.m_class);
        }

        static void VehicleChoice(ref CitizenInstance citizenData, ref VehicleInfo __result, ref VehicleInfo trailer)
        {
            if (!Sim.Active || __result == null || !Settings.Homeless.value || !Housing.Car(citizenData.m_citizen, out bool car)) return;
            if (car && __result.m_vehicleType != VehicleInfo.VehicleType.Bicycle && !Housing.NearHome(ref citizenData)) return;
            __result = null;
            trailer = null;
        }

        static void PathRules(ref CitizenInstance citizenData)
        {
            if (!Sim.Active || !Settings.Homeless.value || !Housing.Tracked(citizenData.m_citizen)) return;
            if ((Singleton<CitizenManager>.instance.m_citizens.m_buffer[citizenData.m_citizen].m_flags & Citizen.Flags.Evacuating) != 0) return;
            citizenData.m_flags |= CitizenInstance.Flags.CannotUseTaxi;
            if (!Housing.FreeTransit(citizenData.GetLastFramePosition())) citizenData.m_flags |= CitizenInstance.Flags.CannotUseTransport;
        }

        static void ResidentialStart(ushort buildingID) => Housing.Current = buildingID;

        static void ResidentialEnd(ushort buildingID, ref Building buildingData)
        {
            Housing.Current = 0;
            if (!Sim.Active) return;
            if (Settings.Homeless.value) Housing.Doorstep(buildingID, ref buildingData);
            if (!Settings.Rent.value) return;
            Housing.LandWatch(buildingID, ref buildingData);
            Housing.RentAlarm(buildingID, ref buildingData);
        }

        static void Problems(ushort buildingID, ref Building buildingData)
        {
            if (Sim.Active) Housing.TaxPressure(buildingID, ref buildingData);
        }

        static bool Growth(ushort blockID)
        {
            if (!Sim.Active || !Housing.ControlledAt(Singleton<ZoneManager>.instance.m_blocks.m_buffer[blockID].m_position)) return true;
            return Sim.Roll(100) >= Housing.SlowGrowth;
        }

        static void Upkeep(RoadBaseAI __instance, ref int __result) => __result = Roads.Apply(__instance, __result);

        static void WornUpkeep(ref int __result) => __result = Wear.Apply(__result);

        static void SegmentStep(ushort segmentID) => Wear.Current = segmentID;

        static void SegmentStepDone() => Wear.Current = 0;

        static void Wages(ref int __result) => __result = Payroll.Apply(__result);

        static void UpkeepPreview(PlayerNetAI __instance, ref int __result)
        {
            if (__instance is RoadBaseAI) __result = Roads.Apply(__instance, __result);
        }

        static void PoliciesBefore(PoliciesPanel __instance) => Guard(() => Ui.PoliciesBefore(__instance));
        static void PoliciesAfter(PoliciesPanel __instance) => Guard(() => Ui.PoliciesAfter(__instance));
        static void ZonedPanel(ZonedBuildingWorldInfoPanel __instance) => Guard(() => Ui.Zoned(__instance));
        static void ServicePanel(CityServiceWorldInfoPanel __instance) => Guard(() => Ui.Service(__instance));

        static void Guard(Action a)
        {
            try { a(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
