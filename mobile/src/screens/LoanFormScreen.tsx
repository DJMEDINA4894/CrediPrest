import type { NativeStackScreenProps } from "@react-navigation/native-stack";
import { useCallback, useState } from "react";
import { Alert, RefreshControl, ScrollView, StyleSheet } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import { api } from "../api/client";
import { Card, EmptyState, ErrorText, Field, PrimaryButton, Screen, SelectField, Text } from "../components/ui";
import type { Client } from "../types/models";
import type { RootStackParamList } from "../navigation/types";
import { colors, spacing } from "../theme/theme";
import { currencyLabels, dateInputValue, lateFeePolicyText } from "../utils/format";

type Props = NativeStackScreenProps<RootStackParamList, "LoanForm">;

const currencies = [
  { value: "1", label: "Cordobas C$" },
  { value: "2", label: "Dolares USD" }
];
const frequencies = [
  { value: "1", label: "Semanal" },
  { value: "2", label: "Quincenal" },
  { value: "3", label: "Mensual" }
];

export function LoanFormScreen({ route, navigation }: Props) {
  const loan = route.params?.loan;
  const [clients, setClients] = useState<Client[]>([]);
  const [clientId, setClientId] = useState(loan?.clientId ?? route.params?.clientId ?? "");
  const [referenceName, setReferenceName] = useState(loan?.referenceName ?? "");
  const [principalAmount, setPrincipalAmount] = useState(loan ? String(loan.principalAmount) : "");
  const [currency, setCurrency] = useState(String(loan?.currency ?? 1));
  const [monthlyInterestRate, setMonthlyInterestRate] = useState(loan ? String(loan.monthlyInterestRate) : "10");
  const [paymentFrequency, setPaymentFrequency] = useState(String(loan?.paymentFrequency ?? 3));
  const [termMonths, setTermMonths] = useState(loan ? String(loan.termMonths) : "1");
  const [startDate, setStartDate] = useState(dateInputValue(loan?.startDate));
  const [notes, setNotes] = useState(loan?.notes ?? "");
  const [agreementCity, setAgreementCity] = useState(loan?.agreementCity ?? "");
  const [lateFeeDescription, setLateFeeDescription] = useState(loan?.lateFeeDescription?.replace("%", "") ?? "50");
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const loadClients = useCallback(async () => {
    try {
      setLoading(true);
      setClients(await api.clients());
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudieron cargar los clientes.");
    } finally {
      setLoading(false);
    }
  }, []);

  useFocusEffect(useCallback(() => {
    void loadClients();
  }, [loadClients]));

  async function save() {
    const amount = Number(principalAmount);
    const interest = Number(monthlyInterestRate);
    const term = Number(termMonths);

    if (!clientId) {
      setError("Selecciona el cliente del prestamo.");
      return;
    }
    if (!Number.isFinite(amount) || amount <= 0) {
      setError("Ingresa un monto prestado valido.");
      return;
    }
    if (!Number.isFinite(interest) || interest < 0) {
      setError("Ingresa una tasa de interes valida.");
      return;
    }
    if (!Number.isInteger(term) || term < 1) {
      setError("La cantidad de pagos debe ser mayor que cero.");
      return;
    }
    if (!/^\d{4}-\d{2}-\d{2}$/.test(startDate)) {
      setError("La fecha de inicio debe usar el formato AAAA-MM-DD.");
      return;
    }

    const payload = {
      clientId,
      principalAmount: amount,
      currency: Number(currency),
      monthlyInterestRate: interest,
      termMonths: term,
      paymentFrequency: Number(paymentFrequency),
      startDate,
      referenceName: referenceName.trim() || undefined,
      notes: notes.trim() || undefined,
      agreementCity: agreementCity.trim() || undefined,
      lateFeeDescription: lateFeeDescription.trim() || "50"
    };

    try {
      setError("");
      setSaving(true);
      const detail = loan
        ? await api.updateLoan(loan.id, { ...payload, status: loan.status })
        : await api.createLoan(payload);
      Alert.alert("Prestamo guardado", "Las cuotas se calcularon y guardaron correctamente.", [{ text: "Ver detalle", onPress: () => navigation.replace("LoanDetail", { loanId: detail.loan.id }) }]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo guardar el prestamo.");
    } finally {
      setSaving(false);
    }
  }

  const selectedFrequency = Number(paymentFrequency);
  const termLabel = selectedFrequency === 1 ? "Cantidad de pagos semanales" : selectedFrequency === 2 ? "Cantidad de pagos quincenales" : "Cantidad de pagos mensuales";
  const lateFeeExplanation = lateFeePolicyText(
    selectedFrequency,
    Number(principalAmount) || 5000,
    Number(monthlyInterestRate) || 0,
    Number(lateFeeDescription) || 0,
    currencyLabels[Number(currency) === 2 ? 2 : 1],
    Number(termMonths) || 1);
  const clientOptions = clients
    .filter((client) => client.isActive || client.id === loan?.clientId)
    .map((client) => ({ value: client.id, label: `${client.fullName} - ${client.identificationNumber}` }));

  return (
    <Screen>
      <ScrollView keyboardShouldPersistTaps="handled" refreshControl={<RefreshControl refreshing={loading} onRefresh={loadClients} />}>
        <ErrorText text={error} />
        <Card title={loan ? "Editar condiciones del prestamo" : "Crear y generar cuotas"}>
          {loan ? <Text style={styles.editHint}>El cliente no se puede cambiar al editar un prestamo existente.</Text> : null}
          {clients.length === 0 ? <EmptyState text="No hay clientes activos disponibles para crear el prestamo." /> : null}
          <SelectField label="Cliente" value={clientId} options={clientOptions} onChange={setClientId} disabled={Boolean(loan)} />
          <Field label="Referencia del prestamo" value={referenceName} onChangeText={setReferenceName} placeholder="Ej. Moto, negocio o emergencia" />
          <Field label="Monto prestado" value={principalAmount} onChangeText={setPrincipalAmount} keyboardType="decimal-pad" placeholder="Ej. 5000" />
          <SelectField label="Moneda" value={currency} options={currencies} onChange={setCurrency} />
          <Field label="Interes mensual (%)" value={monthlyInterestRate} onChangeText={setMonthlyInterestRate} keyboardType="decimal-pad" placeholder="Ej. 10" />
          <SelectField label="Frecuencia de pago" value={paymentFrequency} options={frequencies} onChange={setPaymentFrequency} />
          <Field label={termLabel} value={termMonths} onChangeText={setTermMonths} keyboardType="number-pad" placeholder="Ej. 6" />
          <Field label="Fecha de inicio" value={startDate} onChangeText={setStartDate} placeholder="AAAA-MM-DD" />
          <Field label="Observaciones" value={notes} onChangeText={setNotes} placeholder="Opcional" multiline />
          <Field label="Ciudad del acuerdo" value={agreementCity} onChangeText={setAgreementCity} placeholder="Ej. Managua" />
          <Field label="Tasa de mora (% de la tasa de interés)" value={lateFeeDescription} onChangeText={setLateFeeDescription} placeholder="Ej. 50" keyboardType="decimal-pad" suffix="%" />
          <Text style={styles.hint}>{lateFeeExplanation}</Text>
          <Text style={styles.hint}>El plazo se calcula por frecuencia: semanal cada 7 dias, quincenal cada 15 dias y mensual cada mes.</Text>
          <PrimaryButton title={loan ? "Guardar cambios" : "Crear y generar cuotas"} onPress={() => void save()} loading={saving} disabled={!loan && clients.length === 0} />
        </Card>
      </ScrollView>
    </Screen>
  );
}

const styles = StyleSheet.create({
  editHint: {
    backgroundColor: colors.soft,
    color: colors.primaryDark,
    marginBottom: spacing.sm,
    padding: spacing.sm
  },
  hint: {
    color: colors.muted,
    fontSize: 12,
    lineHeight: 18,
    marginBottom: spacing.sm
  }
});
