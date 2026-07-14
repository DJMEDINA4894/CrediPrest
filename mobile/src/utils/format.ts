import type { CurrencyType, Installment, InstallmentStatus, LoanDetail } from "../types/models";

export const currencyLabels: Record<CurrencyType, string> = {
  1: "C$",
  2: "USD"
};

export const statusLabels = {
  1: "Activo",
  2: "Cancelado",
  3: "Vencido"
};

export const installmentStatusLabels = {
  1: "Pendiente",
  2: "Parcial",
  3: "Pagada",
  4: "Retrasada"
};

export function money(value: number, currency: string = "C$") {
  return `${currency} ${value.toLocaleString("es-NI", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

export function dateOnly(value: string) {
  const datePart = value.split("T")[0];
  const [year, month, day] = datePart.split("-").map(Number);
  const date = year && month && day ? new Date(year, month - 1, day) : new Date(value);

  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return new Intl.DateTimeFormat("es-NI", { day: "numeric", month: "long", year: "numeric" }).format(date);
}

export function installmentPendingAmount(installment: Installment) {
  return Math.max(0, installment.paymentAmount - installment.amountPaid);
}

export function canMakeExtraordinaryPayment(detail: LoanDetail) {
  if (detail.loan.status !== 1 || detail.loan.pendingBalance <= 0 || detail.loan.lateFeesPending > 0) {
    return false;
  }

  const today = dateInputValue();
  const hasUnpaidLateFee = detail.charges.some((charge) => charge.pendingAmount > 0 || charge.amountPaid < charge.amount);
  const hasIrregularInstallment = detail.installments.some((installment) => {
    const isPending = installment.amountPaid < installment.paymentAmount;
    const isPartial = installment.amountPaid > 0 && isPending;
    const isOverdue = isPending && installment.dueDate.slice(0, 10) < today;
    return isPartial || isOverdue;
  });

  return !hasUnpaidLateFee && !hasIrregularInstallment;
}

function lateFeePeriodSize(paymentFrequency: number) {
  return paymentFrequency === 1 ? 4 : paymentFrequency === 2 ? 2 : 1;
}

function installmentPeriodNumber(paymentFrequency: number, installmentNumber: number) {
  return Math.floor((installmentNumber - 1) / lateFeePeriodSize(paymentFrequency)) + 1;
}

function roundedCurrency(value: number) {
  return Math.round((value + Number.EPSILON) * 100) / 100;
}

export function lateFeeAllocation(detail: LoanDetail, installment: Installment) {
  const periodSize = lateFeePeriodSize(detail.loan.paymentFrequency);
  const periodNumber = installmentPeriodNumber(detail.loan.paymentFrequency, installment.installmentNumber);
  const periodInstallments = detail.installments.filter((item) =>
    installmentPeriodNumber(detail.loan.paymentFrequency, item.installmentNumber) === periodNumber
  );
  const periodIndex = periodInstallments.findIndex((item) => item.id === installment.id);
  const charge = detail.charges.find((item) => item.periodNumber === periodNumber);

  if (!charge || periodIndex < 0) {
    return { amount: 0, amountPaid: 0, pendingAmount: 0 };
  }

  const allocation = charge.allocations?.find((item) => item.installmentId === installment.id);
  if (allocation) {
    return {
      amount: allocation.amount,
      amountPaid: allocation.amountPaid,
      pendingAmount: allocation.pendingAmount
    };
  }

  const regularShare = roundedCurrency(charge.amount / periodSize);
  const amount = periodIndex === periodSize - 1
    ? roundedCurrency(charge.amount - regularShare * (periodSize - 1))
    : regularShare;
  const paidBefore = periodInstallments.slice(0, periodIndex).reduce((sum, item, index) => {
    const previousAmount = index === periodSize - 1
      ? roundedCurrency(charge.amount - regularShare * (periodSize - 1))
      : regularShare;
    return sum + Math.min(previousAmount, Math.max(0, charge.amountPaid - sum));
  }, 0);
  const amountPaid = roundedCurrency(Math.min(amount, Math.max(0, charge.amountPaid - paidBefore)));

  return {
    amount,
    amountPaid,
    pendingAmount: roundedCurrency(Math.max(0, amount - amountPaid))
  };
}

export function lateFeePolicyText(
  paymentFrequency: number,
  principalAmount = 5000,
  monthlyInterestRate = 10,
  lateFeePercentage = 50,
  currency = "C$",
  termMonths = 1
) {
  const installmentCount = Number.isFinite(termMonths) && termMonths > 0 ? termMonths : 1;
  const monthlyLateFeeRate = monthlyInterestRate * lateFeePercentage / 100;
  const periodSize = paymentFrequency === 1 ? 4 : paymentFrequency === 2 ? 2 : 1;
  const interestMonths = paymentFrequency === 1 ? installmentCount / 4 : paymentFrequency === 2 ? installmentCount / 2 : installmentCount;
  const totalToPay = principalAmount + principalAmount * monthlyInterestRate / 100 * interestMonths;
  const installmentAmount = totalToPay / installmentCount;
  const installmentsInPeriod = Math.min(periodSize, installmentCount);
  const monthlyPeriodAmount = installmentAmount * installmentsInPeriod;
  const fixedLateFee = monthlyPeriodAmount * monthlyLateFeeRate / 100;
  const frequencyText = paymentFrequency === 1
    ? `se reparte entre ${installmentsInPeriod} cuotas semanales`
    : paymentFrequency === 2
      ? `se reparte entre ${installmentsInPeriod} cuotas quincenales`
      : "se carga en la cuota mensual";
  return `La mora es fija y no crece por dias. Al cerrar el periodo mensual se aplica ${monthlyLateFeeRate}% al monto pendiente de ese periodo. Ejemplo: sobre ${money(monthlyPeriodAmount, currency)}, la mora seria ${money(fixedLateFee, currency)} y ${frequencyText}${installmentsInPeriod > 1 ? `: ${money(fixedLateFee / installmentsInPeriod, currency)} por cuota` : ""}.`;
}

export function effectiveInstallmentStatus(installment: Installment): InstallmentStatus {
  if (installment.amountPaid >= installment.paymentAmount) {
    return 3;
  }

  if (installment.dueDate.slice(0, 10) < dateInputValue()) {
    return 4;
  }

  return installment.amountPaid > 0 ? 2 : 1;
}

export function dateInputValue(value?: string) {
  return value ? value.slice(0, 10) : new Date().toISOString().slice(0, 10);
}
