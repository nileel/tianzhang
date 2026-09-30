using System;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.Adventure
{
    /// <summary>
    /// Isolated, non-production fixture for the single JD_COMBO_FIRE_FERRY_LINE_01 scenario.
    /// It owns only the fixture's local source, ferry-line ledger, markers, and END cleanup.
    /// </summary>
    public sealed class JindanFireFerryLineScenarioController : MonoBehaviour
    {
        public const string FixtureIdValue = "JD_COMBO_FIRE_FERRY_LINE_01_SCENE_FIXTURE";

        [Header("Serialized fixture identity and route")]
        [SerializeField] private string fixtureId = FixtureIdValue;
        [SerializeField] private Vector2Int sourceCell = new Vector2Int(0, 0);
        [SerializeField] private Vector2Int formationEyeCell = new Vector2Int(0, 1);
        [SerializeField] private Vector2Int[] ferryRoute =
        {
            new Vector2Int(1, 0),
            new Vector2Int(2, 0),
            new Vector2Int(3, 0),
        };

        [Header("Serialized fixture costs and capacities")]
        [SerializeField] private int initialActions = 2;
        [SerializeField] private int initialSpirit = 4;
        [SerializeField] private int initialFuel = 2;
        [SerializeField] private int initialFireBudget = 3;
        [SerializeField] private int initialSurfaceCapacity = 1;
        [SerializeField] private int initialEnvironmentCapacity = 1;
        [SerializeField] private int manifestActionCost = 1;
        [SerializeField] private int manifestSpiritCost = 1;
        [SerializeField] private int manifestFuelCost = 1;
        [SerializeField] private int manifestSurfaceCapacityCost = 1;
        [SerializeField] private int escalateActionCost = 1;
        [SerializeField] private int escalateSpiritCost = 1;
        [SerializeField] private int escalateFuelCost = 1;
        [SerializeField] private int fireBudgetCostPerNode = 1;
        [SerializeField] private int environmentCapacityCost = 1;

        [Header("Serialized fixture countermeasures")]
        [SerializeField] private bool formationEyeActive = true;
        [SerializeField] private bool waterBlocked;
        [SerializeField] private bool routeIsolated;
        [SerializeField] private bool fuelDisconnected;
        [SerializeField] private bool forceNonAdjacentRoute;
        [SerializeField] private bool forceFireBudgetInsufficient;
        [SerializeField] private bool forceSurfaceCapacityInsufficient;
        [SerializeField] private bool forceEnvironmentCapacityInsufficient;

        [Header("Serialized fixture presentation")]
        [SerializeField] private GameObject formationEyeMarker;
        [SerializeField] private GameObject sourceMarker;
        [SerializeField] private GameObject[] ferryLineMarkers = Array.Empty<GameObject>();
        [SerializeField] private Text stateText;

        private bool initialized;
        private bool sourceActive;
        private bool ferryLineActive;
        private bool ended;
        private string sourceRef;
        private string lastFailureReason;
        private int remainingActions;
        private int remainingSpirit;
        private int remainingFuel;
        private int remainingFireBudget;
        private int availableSurfaceCapacity;
        private int availableEnvironmentCapacity;
        private int occupiedSurfaceCapacity;
        private int occupiedEnvironmentCapacity;
        private bool countermeasureDefaultsCaptured;
        private bool defaultFormationEyeActive;
        private bool defaultWaterBlocked;
        private bool defaultRouteIsolated;
        private bool defaultFuelDisconnected;
        private bool defaultForceNonAdjacentRoute;
        private bool defaultForceFireBudgetInsufficient;
        private bool defaultForceSurfaceCapacityInsufficient;
        private bool defaultForceEnvironmentCapacityInsufficient;

        public string FixtureId => fixtureId;
        public Vector2Int SourceCell => sourceCell;
        public Vector2Int FormationEyeCell => formationEyeCell;
        public int RouteNodeCount => ferryRoute == null ? 0 : ferryRoute.Length;
        public bool HasSourceRef => sourceActive && !string.IsNullOrEmpty(sourceRef);
        public bool HasFerryLine => ferryLineActive;
        public bool HasEnded => ended;
        public string SourceRef => sourceRef;
        public string LastFailureReason => lastFailureReason;
        public JindanFireFerryLineScenarioLedger Ledger => CaptureLedger();

        private void Awake()
        {
            CaptureCountermeasureDefaults();
            ResetFixtureState();
        }

        private void OnDisable()
        {
            if (initialized) EndScenario();
        }

        /// <summary>Restores this already-serialized fixture to its initial local test state.</summary>
        public void ResetFixtureState()
        {
            CaptureCountermeasureDefaults();
            RestoreCountermeasureDefaults();
            initialized = true;
            sourceActive = false;
            ferryLineActive = false;
            ended = false;
            sourceRef = null;
            lastFailureReason = null;
            remainingActions = initialActions;
            remainingSpirit = initialSpirit;
            remainingFuel = initialFuel;
            remainingFireBudget = initialFireBudget;
            availableSurfaceCapacity = initialSurfaceCapacity;
            availableEnvironmentCapacity = initialEnvironmentCapacity;
            occupiedSurfaceCapacity = 0;
            occupiedEnvironmentCapacity = 0;
            SetMarkerActive(sourceMarker, false);
            SetFerryLineMarkers(false);
            SetMarkerActive(formationEyeMarker, formationEyeActive);
            RefreshStatus();
        }

        /// <summary>
        /// Binds the presentation objects for this already-local fixture. The Builder persists
        /// the same references into the non-BuildSettings scene; PlayMode tests use it only to
        /// exercise the identical isolated fixture lifecycle without altering BuildSettings.
        /// </summary>
        public void ConfigureFixturePresentation(
            GameObject fixtureFormationEyeMarker,
            GameObject fixtureSourceMarker,
            GameObject[] fixtureFerryLineMarkers,
            Text fixtureStateText)
        {
            formationEyeMarker = fixtureFormationEyeMarker;
            sourceMarker = fixtureSourceMarker;
            ferryLineMarkers = fixtureFerryLineMarkers ?? Array.Empty<GameObject>();
            stateText = fixtureStateText;
            countermeasureDefaultsCaptured = false;
            ResetFixtureState();
        }

        public bool TryManifest()
        {
            if (ended) return Fail("scenario_ended");
            if (sourceActive) return Fail("manifest_source_already_active");
            if (!HasValidManifestFixture()) return Fail("manifest_fixture_invalid");
            if (forceSurfaceCapacityInsufficient || availableSurfaceCapacity < manifestSurfaceCapacityCost)
                return Fail("manifest_surface_capacity_insufficient");
            if (remainingActions < manifestActionCost) return Fail("manifest_action_insufficient");
            if (remainingSpirit < manifestSpiritCost) return Fail("manifest_spirit_insufficient");
            if (remainingFuel < manifestFuelCost) return Fail("manifest_fuel_insufficient");

            remainingActions -= manifestActionCost;
            remainingSpirit -= manifestSpiritCost;
            remainingFuel -= manifestFuelCost;
            availableSurfaceCapacity -= manifestSurfaceCapacityCost;
            occupiedSurfaceCapacity += manifestSurfaceCapacityCost;
            sourceActive = true;
            sourceRef = fixtureId + ":source:" + sourceCell.x + ":" + sourceCell.y;
            lastFailureReason = null;
            SetMarkerActive(sourceMarker, true);
            RefreshStatus();
            return true;
        }

        public bool TryEscalate()
        {
            if (ended) return Fail("scenario_ended");
            if (!sourceActive || string.IsNullOrEmpty(sourceRef)) return Fail("escalate_source_missing");
            if (ferryLineActive) return Fail("escalate_ferry_line_already_active");
            if (!formationEyeActive) return Fail("escalate_formation_eye_invalid");
            if (waterBlocked) return Fail("escalate_route_water_blocked");
            if (routeIsolated) return Fail("escalate_route_isolated");
            if (fuelDisconnected) return Fail("escalate_route_fuel_disconnected");
            if (forceNonAdjacentRoute || !IsContinuousFerryRoute()) return Fail("escalate_route_non_adjacent");
            if (!HasValidEscalateCosts()) return Fail("escalate_fixture_invalid");

            int fireBudgetCost = checked(RouteNodeCount * fireBudgetCostPerNode);
            if (forceFireBudgetInsufficient || remainingFireBudget < fireBudgetCost)
                return Fail("escalate_fire_budget_insufficient");
            if (forceEnvironmentCapacityInsufficient || availableEnvironmentCapacity < environmentCapacityCost)
                return Fail("escalate_environment_capacity_insufficient");
            if (remainingActions < escalateActionCost) return Fail("escalate_action_insufficient");
            if (remainingSpirit < escalateSpiritCost) return Fail("escalate_spirit_insufficient");
            if (remainingFuel < escalateFuelCost) return Fail("escalate_fuel_insufficient");

            remainingActions -= escalateActionCost;
            remainingSpirit -= escalateSpiritCost;
            remainingFuel -= escalateFuelCost;
            remainingFireBudget -= fireBudgetCost;
            availableEnvironmentCapacity -= environmentCapacityCost;
            occupiedEnvironmentCapacity += environmentCapacityCost;
            ferryLineActive = true;
            lastFailureReason = null;
            SetFerryLineMarkers(true);
            RefreshStatus();
            return true;
        }

        /// <summary>Applies the fixture's shared END boundary without refunding settled costs.</summary>
        public void EndScenario()
        {
            if (!initialized || ended) return;

            sourceActive = false;
            ferryLineActive = false;
            ended = true;
            sourceRef = null;
            availableSurfaceCapacity += occupiedSurfaceCapacity;
            availableEnvironmentCapacity += occupiedEnvironmentCapacity;
            occupiedSurfaceCapacity = 0;
            occupiedEnvironmentCapacity = 0;
            SetMarkerActive(sourceMarker, false);
            SetFerryLineMarkers(false);
            RefreshStatus();
        }

        public void ExtinguishSource()
        {
            if (!ended) EndScenario();
        }

        public void SetFormationEyeActive(bool value)
        {
            formationEyeActive = value;
            SetMarkerActive(formationEyeMarker, value);
            EnforceEndBoundaryIfFerryLineIsInvalid();
        }

        public void SetWaterBlocked(bool value)
        {
            waterBlocked = value;
            EnforceEndBoundaryIfFerryLineIsInvalid();
        }

        public void SetRouteIsolated(bool value)
        {
            routeIsolated = value;
            EnforceEndBoundaryIfFerryLineIsInvalid();
        }

        public void SetFuelDisconnected(bool value)
        {
            fuelDisconnected = value;
            EnforceEndBoundaryIfFerryLineIsInvalid();
        }

        public void SetForceNonAdjacentRoute(bool value)
        {
            forceNonAdjacentRoute = value;
            EnforceEndBoundaryIfFerryLineIsInvalid();
        }

        public void SetForceFireBudgetInsufficient(bool value)
        {
            forceFireBudgetInsufficient = value;
            EnforceEndBoundaryIfFerryLineIsInvalid();
        }

        public void SetForceSurfaceCapacityInsufficient(bool value)
        {
            forceSurfaceCapacityInsufficient = value;
            EnforceEndBoundaryIfFerryLineIsInvalid();
        }

        public void SetForceEnvironmentCapacityInsufficient(bool value)
        {
            forceEnvironmentCapacityInsufficient = value;
            EnforceEndBoundaryIfFerryLineIsInvalid();
        }

        public bool IsFerryLineMarkerVisible(int index)
        {
            return ferryLineMarkers != null && index >= 0 && index < ferryLineMarkers.Length &&
                ferryLineMarkers[index] != null && ferryLineMarkers[index].activeSelf;
        }

        public JindanFireFerryLineScenarioLedger CaptureLedger()
        {
            return new JindanFireFerryLineScenarioLedger(
                remainingActions,
                remainingSpirit,
                remainingFuel,
                remainingFireBudget,
                availableSurfaceCapacity,
                availableEnvironmentCapacity,
                occupiedSurfaceCapacity,
                occupiedEnvironmentCapacity,
                sourceActive,
                ferryLineActive,
                ended);
        }

        private bool HasValidManifestFixture()
        {
            return string.Equals(fixtureId, FixtureIdValue, StringComparison.Ordinal) &&
                sourceMarker != null && manifestActionCost >= 0 && manifestSpiritCost >= 0 &&
                manifestFuelCost >= 0 && manifestSurfaceCapacityCost > 0;
        }

        private bool HasValidEscalateCosts()
        {
            return ferryRoute != null && ferryRoute.Length > 0 && ferryLineMarkers != null &&
                ferryLineMarkers.Length == ferryRoute.Length && escalateActionCost >= 0 &&
                escalateSpiritCost >= 0 && escalateFuelCost >= 0 && fireBudgetCostPerNode > 0 &&
                environmentCapacityCost > 0;
        }

        private bool IsContinuousFerryRoute()
        {
            if (ferryRoute == null || ferryRoute.Length == 0 || !AreAdjacent(sourceCell, ferryRoute[0]))
                return false;

            for (int index = 1; index < ferryRoute.Length; index++)
                if (!AreAdjacent(ferryRoute[index - 1], ferryRoute[index])) return false;
            return true;
        }

        private static bool AreAdjacent(Vector2Int left, Vector2Int right)
        {
            int deltaQ = left.x - right.x;
            int deltaR = left.y - right.y;
            return (Math.Abs(deltaQ) + Math.Abs(deltaR) + Math.Abs(deltaQ + deltaR)) / 2 == 1;
        }

        private bool Fail(string reason)
        {
            lastFailureReason = reason;
            RefreshStatus();
            return false;
        }

        private void EnforceEndBoundaryIfFerryLineIsInvalid()
        {
            if (ferryLineActive && (!formationEyeActive || waterBlocked || routeIsolated ||
                                    fuelDisconnected || forceNonAdjacentRoute ||
                                    forceFireBudgetInsufficient || forceSurfaceCapacityInsufficient ||
                                    forceEnvironmentCapacityInsufficient))
            {
                EndScenario();
                return;
            }
            RefreshStatus();
        }

        private void SetFerryLineMarkers(bool active)
        {
            if (ferryLineMarkers == null) return;
            foreach (GameObject marker in ferryLineMarkers) SetMarkerActive(marker, active);
        }

        private static void SetMarkerActive(GameObject marker, bool active)
        {
            if (marker != null) marker.SetActive(active);
        }

        private void RefreshStatus()
        {
            if (stateText == null) return;
            stateText.text = fixtureId + "\nsource=" + (sourceActive ? sourceRef : "none") +
                "; ferryLine=" + ferryLineActive + "; ended=" + ended +
                "\nactions=" + remainingActions + "; spirit=" + remainingSpirit +
                "; fuel=" + remainingFuel + "; fireBudget=" + remainingFireBudget +
                "\nsurface=" + occupiedSurfaceCapacity + "; environment=" + occupiedEnvironmentCapacity +
                (string.IsNullOrEmpty(lastFailureReason) ? string.Empty : "\nreason=" + lastFailureReason);
        }

        private void CaptureCountermeasureDefaults()
        {
            if (countermeasureDefaultsCaptured) return;
            countermeasureDefaultsCaptured = true;
            defaultFormationEyeActive = formationEyeActive;
            defaultWaterBlocked = waterBlocked;
            defaultRouteIsolated = routeIsolated;
            defaultFuelDisconnected = fuelDisconnected;
            defaultForceNonAdjacentRoute = forceNonAdjacentRoute;
            defaultForceFireBudgetInsufficient = forceFireBudgetInsufficient;
            defaultForceSurfaceCapacityInsufficient = forceSurfaceCapacityInsufficient;
            defaultForceEnvironmentCapacityInsufficient = forceEnvironmentCapacityInsufficient;
        }

        private void RestoreCountermeasureDefaults()
        {
            formationEyeActive = defaultFormationEyeActive;
            waterBlocked = defaultWaterBlocked;
            routeIsolated = defaultRouteIsolated;
            fuelDisconnected = defaultFuelDisconnected;
            forceNonAdjacentRoute = defaultForceNonAdjacentRoute;
            forceFireBudgetInsufficient = defaultForceFireBudgetInsufficient;
            forceSurfaceCapacityInsufficient = defaultForceSurfaceCapacityInsufficient;
            forceEnvironmentCapacityInsufficient = defaultForceEnvironmentCapacityInsufficient;
        }
    }

    public struct JindanFireFerryLineScenarioLedger
    {
        public JindanFireFerryLineScenarioLedger(
            int actions,
            int spirit,
            int fuel,
            int fireBudget,
            int availableSurfaceCapacity,
            int availableEnvironmentCapacity,
            int occupiedSurfaceCapacity,
            int occupiedEnvironmentCapacity,
            bool sourceActive,
            bool ferryLineActive,
            bool ended)
        {
            Actions = actions;
            Spirit = spirit;
            Fuel = fuel;
            FireBudget = fireBudget;
            AvailableSurfaceCapacity = availableSurfaceCapacity;
            AvailableEnvironmentCapacity = availableEnvironmentCapacity;
            OccupiedSurfaceCapacity = occupiedSurfaceCapacity;
            OccupiedEnvironmentCapacity = occupiedEnvironmentCapacity;
            SourceActive = sourceActive;
            FerryLineActive = ferryLineActive;
            Ended = ended;
        }

        public int Actions { get; }
        public int Spirit { get; }
        public int Fuel { get; }
        public int FireBudget { get; }
        public int AvailableSurfaceCapacity { get; }
        public int AvailableEnvironmentCapacity { get; }
        public int OccupiedSurfaceCapacity { get; }
        public int OccupiedEnvironmentCapacity { get; }
        public bool SourceActive { get; }
        public bool FerryLineActive { get; }
        public bool Ended { get; }
    }
}
