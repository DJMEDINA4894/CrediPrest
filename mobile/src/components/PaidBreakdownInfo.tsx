import { InfoTooltip } from "./ui";
import { money } from "../utils/format";

export function PaidBreakdownInfo({
  principal,
  interest,
  currency,
  message
}: {
  principal?: number;
  interest?: number;
  currency?: string;
  message?: string;
}) {
  const detail = message
    ?? `Capital pagado: ${money(principal ?? 0, currency)}\nInterés pagado: ${money(interest ?? 0, currency)}`;

  return <InfoTooltip title="Detalle de lo pagado" message={detail} />;
}
