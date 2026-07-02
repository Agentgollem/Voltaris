using UnityEngine;

/// <summary>
/// One terminal assembly of a substation transformer.
/// The same script is used on both sides — the parent <see cref="Transformer"/>
/// tells each instance whether it is Side A or Side B via <see cref="IsSideA"/>.
///
/// Place two child GameObjects under the Transformer root, attach this script
/// to each, and nest their ConnectionPoints underneath them.
///
///   Transformer root          (Transformer script)
///   ├── Side_A                (TransformerSide, auto-found first)
///   │   ├── CP_A1             (ConnectionPoint, nominalVoltageKV = e.g. 400)
///   │   └── CP_A2             (ConnectionPoint, nominalVoltageKV = e.g. 400)
///   └── Side_B                (TransformerSide, auto-found second)
///       ├── CP_B1             (ConnectionPoint, nominalVoltageKV = e.g. 11)
///       └── CP_B2             (ConnectionPoint, nominalVoltageKV = e.g. 11)
///
/// In the BFS each side ends up in its own PowerNetwork (there is no internal
/// connection between them), so two electrically separate islands are maintained
/// even though they belong to the same physical device.
///
/// This side appears to its network as:
///   • A consumer  when power is flowing INTO this side from the other side.
///   • A producer  when power is flowing OUT of this side toward the other side.
/// The direction is resolved automatically each tick by Transformer.ResolveTransfer.
/// </summary>
[AddComponentMenu("Power Grid/Transformer/Transformer Side")]
public class TransformerSide : ElectricalNode
{
  [Header("Voltage")]
  [Tooltip("Nominal voltage at this terminal in kV (for display only). " +
           "Does not affect which networks can be wired together.")]
  [SerializeField] private float nominalVoltageKV = 132f;

  // ── Set by Transformer.Awake() ─────────────────────────────────────────
  internal Transformer ParentTransformer { get; set; }
  /// True when this instance is the 'A' terminal; false for 'B'.
  internal bool IsSideA { get; set; }

  public float NominalVoltageKV => nominalVoltageKV;

  // ── ElectricalNode overrides ───────────────────────────────────────────

  public override float GetProductionMW() =>
      ParentTransformer == null ? 0f
      : IsSideA ? ParentTransformer.SideAProductionMW
                : ParentTransformer.SideBProductionMW;

  public override float GetConsumptionMW() =>
      ParentTransformer == null ? 0f
      : IsSideA ? ParentTransformer.SideAConsumptionMW
                : ParentTransformer.SideBConsumptionMW;

  /// Returns the nominal voltage when this side is actively producing power,
  /// so PowerNetwork.Recalculate() can set VoltageKV for the downstream network.
  public override float GetProductionKV() =>
      GetProductionMW() > 0f ? nominalVoltageKV : 0f;
}