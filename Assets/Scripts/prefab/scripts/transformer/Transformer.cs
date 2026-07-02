using UnityEngine;

/// <summary>
/// Transfers power between two electrically isolated networks.
/// Works as both a step-up and a step-down transformer depending on which
/// side has a surplus and which has a deficit — no manual direction setting
/// is required.  The direction is resolved automatically each simulation tick.
///
/// Power flow convention
/// ─────────────────────
///   transferMW > 0  →  A → B  (SideA is the input,  SideB is the output)
///   transferMW < 0  →  B → A  (SideB is the input,  SideA is the output)
///   transferMW = 0  →  offline or neither side has a net surplus
///
/// The input side appears as a consumer on its network (drawing |transferMW|).
/// The output side appears as a producer on its network (injecting |transferMW|
/// × efficiency), so efficiency losses show up as a permanent small deficit on
/// the receiving network, lowering its frequency slightly — the correct
/// physical behaviour for a simplified simulation.
///
/// Extensibility
/// ─────────────
/// • Overload alerts : check LoadingPercent > 100 externally.
/// • Protection relay: set IsOnline = false to trip the transformer.
/// • Tap-changer      : scale TransformerSide.NominalVoltageKV at runtime.
/// • Maintenance mode : subclass and override ResolveTransfer / SetTransfer.
/// </summary>
[AddComponentMenu("Power Grid/Transformer")]
public class Transformer : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────

    [Header("Side References (auto-discovered from children if left empty)")]
    [SerializeField] private TransformerSide sideA;
    [SerializeField] private TransformerSide sideB;

    [Header("Ratings")]
    [Tooltip("Maximum power transfer in MW. Demand above this is shed on the receiving side.")]
    [SerializeField] private float ratedCapacityMW = 100f;

    [Tooltip("Fraction of input power delivered to the output side (0.95 – 0.99 typical).")]
    [SerializeField, Range(0f, 1f)] private float efficiency = 0.98f;

    [Header("Control")]
    [Tooltip("Uncheck to take the transformer offline; both sides then produce and consume nothing.")]
    [SerializeField] private bool isOnline = true;

    // ── Runtime state ─────────────────────────────────────────────────────
    // Serialized for Inspector readability during play mode; do not edit.

    [Header("Runtime — read only in play mode")]
    [SerializeField] private float currentTransferMW;   // + = A→B, − = B→A
    [SerializeField] private float loadingPercent;
    [SerializeField] private float lossesKW;
    [SerializeField] private string flowDirection = "Idle";

    // ── Internal properties read by TransformerSide ───────────────────────

    /// Power SideA injects into network A (only when B → A flow).
    internal float SideAProductionMW  => currentTransferMW < 0f
        ? -currentTransferMW * efficiency : 0f;

    /// Power SideA draws from network A (only when A → B flow).
    internal float SideAConsumptionMW => currentTransferMW > 0f
        ? currentTransferMW : 0f;

    /// Power SideB injects into network B (only when A → B flow).
    internal float SideBProductionMW  => currentTransferMW > 0f
        ? currentTransferMW * efficiency : 0f;

    /// Power SideB draws from network B (only when B → A flow).
    internal float SideBConsumptionMW => currentTransferMW < 0f
        ? -currentTransferMW : 0f;

    // ── Public API ────────────────────────────────────────────────────────

    public bool  IsOnline        => isOnline;
    public float RatedCapacityMW => ratedCapacityMW;
    public float Efficiency      => efficiency;

    /// Signed transfer this tick (MW). Positive = A→B, negative = B→A.
    public float CurrentTransferMW => currentTransferMW;

    /// Absolute loading as a percentage of rated capacity (0 – 100+).
    public float LoadingPercent    => loadingPercent;

    /// Transformer losses this tick (kW).
    public float LossesKW          => lossesKW;

    // Exposed so PowerGridManager can look up each side's network.
    public TransformerSide SideA => sideA;
    public TransformerSide SideB => sideB;

    // ── Unity ─────────────────────────────────────────────────────────────

    protected virtual void Awake()
    {
        // Auto-discover the two sides from child GameObjects if not assigned.
        if (sideA == null || sideB == null)
        {
            var sides = GetComponentsInChildren<TransformerSide>();
            if (sideA == null && sides.Length > 0) sideA = sides[0];
            if (sideB == null && sides.Length > 1) sideB = sides[1];
        }

        // Wire back-references so each side can call us during GetProductionMW /
        // GetConsumptionMW.  Runs before any Start() fires.
        if (sideA != null) { sideA.ParentTransformer = this; sideA.IsSideA = true;  }
        if (sideB != null) { sideB.ParentTransformer = this; sideB.IsSideA = false; }

        if (sideA == null)
            Debug.LogError($"[Transformer] '{name}': SideA not found.", gameObject);
        if (sideB == null)
            Debug.LogError($"[Transformer] '{name}': SideB not found.", gameObject);
    }

    protected virtual void Start()
    {
        PowerGridManager.Instance?.RegisterTransformer(this);
    }

    protected virtual void OnDestroy()
    {
        PowerGridManager.Instance?.UnregisterTransformer(this);
    }

    // ── Simulation ────────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="PowerGridManager"/> between the two Recalculate passes.
    ///
    /// Reads the pass-1 network snapshots (which still include the previous tick's
    /// transformer contributions) and subtracts those contributions to recover
    /// each network's native surplus.  Power then flows from whichever side has
    /// a native surplus to whichever side has a native deficit, capped at the
    /// rated capacity.
    ///
    /// If both sides have a surplus or both have a deficit, no transfer occurs.
    /// </summary>
    public void ResolveTransfer(PowerNetwork netA, PowerNetwork netB)
    {
        if (!isOnline || netA == null || netB == null)
        {
            SetTransfer(0f);
            return;
        }

        // Remove this transformer's previous-tick contributions so we see
        // the "native" surplus/deficit of each network without us in it.
        //
        // prevA_prod  = what we injected into netA last tick (B→A case)
        // prevA_cons  = what we drew  from netA last tick (A→B case)
        float prevAProduction  = currentTransferMW < 0f ? -currentTransferMW * efficiency : 0f;
        float prevAConsumption = currentTransferMW > 0f ?  currentTransferMW             : 0f;
        float prevBProduction  = currentTransferMW > 0f ?  currentTransferMW * efficiency : 0f;
        float prevBConsumption = currentTransferMW < 0f ? -currentTransferMW             : 0f;

        float nativeSurplusA = (netA.ProductionMW - prevAProduction)
                             - (netA.ConsumptionMW - prevAConsumption);

        float nativeSurplusB = (netB.ProductionMW - prevBProduction)
                             - (netB.ConsumptionMW - prevBConsumption);

        if (nativeSurplusA > 0f && nativeSurplusB < 0f)
        {
            // A has spare generation, B needs power → flow A → B
            float transfer = Mathf.Min(nativeSurplusA, -nativeSurplusB);
            transfer = Mathf.Min(transfer, ratedCapacityMW);
            SetTransfer(transfer);          // positive
        }
        else if (nativeSurplusB > 0f && nativeSurplusA < 0f)
        {
            // B has spare generation, A needs power → flow B → A
            float transfer = Mathf.Min(nativeSurplusB, -nativeSurplusA);
            transfer = Mathf.Min(transfer, ratedCapacityMW);
            SetTransfer(-transfer);         // negative
        }
        else
        {
            // Both surplus or both deficit — no useful transfer possible.
            SetTransfer(0f);
        }
    }

    private void SetTransfer(float signedMW)
    {
        currentTransferMW = signedMW;
        float absMW    = Mathf.Abs(signedMW);
        float outputMW = absMW * efficiency;
        lossesKW       = (absMW - outputMW) * 1_000f;
        loadingPercent = ratedCapacityMW > 0f
            ? absMW / ratedCapacityMW * 100f
            : 0f;
        flowDirection  = signedMW >  0.001f ? "A -> B"
                       : signedMW < -0.001f ? "B -> A"
                       :                      "Idle";
    }

    // ── Debug ─────────────────────────────────────────────────────────────

    public override string ToString() =>
        $"[Transformer '{name}']  {flowDirection}  " +
        $"|T|={Mathf.Abs(currentTransferMW):F1} MW  " +
        $"Loss={lossesKW:F0} kW  Load={loadingPercent:F0}%  " +
        $"Efficiency={efficiency:P0}  Online={isOnline}";
}