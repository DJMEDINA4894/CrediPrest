import type { NativeStackScreenProps } from "@react-navigation/native-stack";
import { useState } from "react";
import { Alert, ScrollView, StyleSheet, Text } from "react-native";
import { api } from "../api/client";
import { Card, ErrorText, Field, PrimaryButton, Screen, SelectField } from "../components/ui";
import type { RootStackParamList } from "../navigation/types";
import type { Client } from "../types/models";
import { colors, spacing } from "../theme/theme";

type Props = NativeStackScreenProps<RootStackParamList, "ClientForm">;

const paymentMethods = [
  { value: "cash", label: "Efectivo" },
  { value: "bac", label: "Transferencia BAC" },
  { value: "lafise", label: "Transferencia Lafise" },
  { value: "bampro", label: "Transferencia Bampro" },
  { value: "kash", label: "Kash" }
];

export function ClientFormScreen({ route, navigation }: Props) {
  const client = route.params?.client;
  const [fullName, setFullName] = useState(client?.fullName ?? "");
  const [identificationNumber, setIdentificationNumber] = useState(client?.identificationNumber ?? "");
  const [phone, setPhone] = useState(client?.phone ?? "");
  const [email, setEmail] = useState(client?.email ?? "");
  const [address, setAddress] = useState(client?.address ?? "");
  const [personalReference1, setPersonalReference1] = useState(client?.personalReference1 ?? "");
  const [referencePhone1, setReferencePhone1] = useState(client?.referencePhone1 ?? "");
  const [preferredPaymentMethod, setPreferredPaymentMethod] = useState(client?.preferredPaymentMethod ?? "cash");
  const [paymentAccount, setPaymentAccount] = useState(accountValue(client, client?.preferredPaymentMethod ?? "cash"));
  const [notes, setNotes] = useState(client?.notes ?? "");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  function updatePaymentMethod(method: string) {
    setPreferredPaymentMethod(method);
    setPaymentAccount(accountValue(client, method));
  }

  async function save() {
    const normalizedPhone = phone.replace(/[^0-9]/g, "");
    const normalizedReferencePhone = referencePhone1.replace(/[^0-9]/g, "");
    const isBankTransfer = preferredPaymentMethod !== "cash" && preferredPaymentMethod !== "kash";

    if (fullName.trim().length < 3) {
      setError("Ingresa el nombre completo del cliente.");
      return;
    }
    if (!/^\d{3}-?\d{6}-?\d{4}[A-Za-z]$/.test(identificationNumber.trim())) {
      setError("La cedula debe tener el formato 001-010101-0001A.");
      return;
    }
    if (normalizedPhone.length < 8 || normalizedPhone.length > 15) {
      setError("El telefono debe tener entre 8 y 15 digitos.");
      return;
    }
    if (email.trim() && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) {
      setError("Ingresa un correo valido.");
      return;
    }
    if (!address.trim()) {
      setError("Ingresa la direccion del cliente.");
      return;
    }
    if (referencePhone1.trim() && (normalizedReferencePhone.length < 8 || normalizedReferencePhone.length > 15)) {
      setError("El telefono de referencia debe tener entre 8 y 15 digitos.");
      return;
    }
    if (preferredPaymentMethod !== "cash" && !paymentAccount.trim()) {
      setError("Ingresa la cuenta o dato de pago seleccionado.");
      return;
    }
    if (isBankTransfer && !/^\d{6,24}$/.test(paymentAccount.trim())) {
      setError("La cuenta bancaria debe tener solo numeros, entre 6 y 24 digitos.");
      return;
    }
    if (preferredPaymentMethod === "kash" && (paymentAccount.trim().length < 3 || paymentAccount.trim().length > 80)) {
      setError("El dato de Kash debe tener entre 3 y 80 caracteres.");
      return;
    }

    const payload = {
      fullName: fullName.trim(),
      identificationNumber: identificationNumber.trim(),
      phone: phone.trim(),
      address: address.trim(),
      email: email.trim() || undefined,
      personalReference1: personalReference1.trim() || undefined,
      referencePhone1: referencePhone1.trim() || undefined,
      personalReference2: undefined,
      referencePhone2: undefined,
      bacAccountNumber: preferredPaymentMethod === "bac" ? paymentAccount.trim() : undefined,
      lafiseAccountNumber: preferredPaymentMethod === "lafise" ? paymentAccount.trim() : undefined,
      bamproAccountNumber: preferredPaymentMethod === "bampro" ? paymentAccount.trim() : undefined,
      preferredPaymentMethod,
      hasKash: preferredPaymentMethod === "kash" && Boolean(paymentAccount.trim()),
      kashAccount: preferredPaymentMethod === "kash" ? paymentAccount.trim() : undefined,
      notes: notes.trim() || undefined,
      isActive: client?.isActive ?? true
    };

    try {
      setError("");
      setSaving(true);
      if (client) {
        await api.updateClient(client.id, payload);
      } else {
        await api.createClient(payload);
      }
      Alert.alert("Cliente guardado", "La informacion del cliente se guardo correctamente.", [{ text: "Aceptar", onPress: () => navigation.goBack() }]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo guardar el cliente.");
    } finally {
      setSaving(false);
    }
  }

  const accountLabel = preferredPaymentMethod === "kash"
    ? "Dato de Kash"
    : preferredPaymentMethod === "cash"
      ? ""
      : `Cuenta ${paymentMethods.find((item) => item.value === preferredPaymentMethod)?.label.replace("Transferencia ", "") ?? ""}`;

  return (
    <Screen>
      <ScrollView keyboardShouldPersistTaps="handled">
        <ErrorText text={error} />
        <Card title={client ? "Datos del cliente" : "Registrar cliente"}>
          <Field label="Nombre completo" value={fullName} onChangeText={setFullName} placeholder="Nombre completo" />
          <Field label="Cedula" value={identificationNumber} onChangeText={setIdentificationNumber} placeholder="001-010101-0001A" autoCapitalize="characters" />
          <Field label="Telefono" value={phone} onChangeText={setPhone} placeholder="8888-8888" keyboardType="phone-pad" />
          <Field label="Correo" value={email} onChangeText={setEmail} placeholder="correo@dominio.com" autoCapitalize="none" keyboardType="email-address" />
          <Field label="Direccion" value={address} onChangeText={setAddress} placeholder="Direccion del cliente" multiline />
          <Field label="Referencia personal" value={personalReference1} onChangeText={setPersonalReference1} placeholder="Nombre de referencia" />
          <Field label="Telefono referencia" value={referencePhone1} onChangeText={setReferencePhone1} placeholder="Telefono de referencia" keyboardType="phone-pad" />
          <SelectField label="Forma de pago preferida" value={preferredPaymentMethod} options={paymentMethods} onChange={updatePaymentMethod} />
          {preferredPaymentMethod !== "cash" ? (
            <Field label={accountLabel} value={paymentAccount} onChangeText={setPaymentAccount} placeholder={preferredPaymentMethod === "kash" ? "Usuario, telefono o cuenta Kash" : "Numero de cuenta"} keyboardType={preferredPaymentMethod === "kash" ? "default" : "number-pad"} />
          ) : <Text style={styles.cashHint}>Este cliente entrega sus pagos en efectivo.</Text>}
          <Field label="Observaciones" value={notes} onChangeText={setNotes} placeholder="Opcional" multiline />
          <PrimaryButton title={client ? "Guardar cambios" : "Crear cliente"} onPress={() => void save()} loading={saving} />
        </Card>
      </ScrollView>
    </Screen>
  );
}

function accountValue(client: Client | undefined, method: string) {
  if (!client) return "";
  if (method === "kash") return client.kashAccount ?? "";
  if (method === "bac") return client.bacAccountNumber ?? "";
  if (method === "lafise") return client.lafiseAccountNumber ?? "";
  if (method === "bampro") return client.bamproAccountNumber ?? "";
  return "";
}

const styles = StyleSheet.create({
  cashHint: {
    color: colors.muted,
    fontSize: 13,
    marginBottom: spacing.sm
  }
});
