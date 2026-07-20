import { InfoTooltip } from "./ui";
import { money } from "../utils/format";

export function PaidBreakdownInfo({
  principal,
  interest,
  currency,
  message,
  kind = "paid",
  lateFees = 0
}: {
  principal?: number;
  interest?: number;
  currency?: string;
  message?: string;
  kind?: "paid" | "pending";
  lateFees?: number;
}) {
  const detail = message
    ?? (kind === "paid"
      ? `Capital pagado: ${money(principal ?? 0, currency)}\nInterés pagado: ${money(interest ?? 0, currency)}`
      : `Capital pendiente: ${money(principal ?? 0, currency)}\nInterés pendiente: ${money(interest ?? 0, currency)}${lateFees > 0 ? `\nMora pendiente: ${money(lateFees, currency)}` : ""}`);

  return <InfoTooltip title={kind === "paid" ? "Detalle de lo pagado" : "Detalle de lo pendiente"} message={detail} />;
}
