import type { NativeStackScreenProps } from "@react-navigation/native-stack";
import { Ionicons } from "@expo/vector-icons";
import { useState } from "react";
import { Alert, ScrollView, StyleSheet } from "react-native";
import { api } from "../api/client";
import { Card, ErrorText, Field, PrimaryButton, Screen, Text } from "../components/ui";
import type { RootStackParamList } from "../navigation/types";
import { colors, spacing } from "../theme/theme";

type Props = NativeStackScreenProps<RootStackParamList, "UserForm">;

export function UserFormScreen({ route, navigation }: Props) {
  const user = route.params?.user;
  const [fullName, setFullName] = useState(user?.fullName ?? "");
  const [userName, setUserName] = useState(user?.userName ?? "");
  const [email, setEmail] = useState(user?.email ?? "");
  const [phone, setPhone] = useState(user?.phone ?? "");
  const [identificationNumber, setIdentificationNumber] = useState(user?.identificationNumber ?? "");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [showConfirmation, setShowConfirmation] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  async function submit() {
    const normalizedPhone = phone.replace(/[^0-9]/g, "");
    const identificationPattern = /^\d{3}-?\d{6}-?\d{4}[A-Za-z]$/;
    const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

    if (!fullName.trim() || !userName.trim() || !email.trim() || !phone.trim() || !identificationNumber.trim()) {
      setError("Completa todos los datos del prestamista.");
      return;
    }
    if (!emailPattern.test(email.trim())) {
      setError("Ingresa un correo válido.");
      return;
    }
    if (normalizedPhone.length < 8 || normalizedPhone.length > 15) {
      setError("El teléfono debe tener de 8 a 15 dígitos.");
      return;
    }
    if (!identificationPattern.test(identificationNumber.trim())) {
      setError("La cédula debe tener el formato 001-010101-0001A.");
      return;
    }
    if (!user && password.length <= 8) {
      setError("La contraseña debe tener más de 8 caracteres.");
      return;
    }
    if (password && password.length <= 8) {
      setError("La contraseña debe tener más de 8 caracteres.");
      return;
    }
    if (password !== confirmPassword) {
      setError("La contraseña y la confirmación no coinciden.");
      return;
    }

    try {
      setError("");
      setSaving(true);
      if (user) {
        await api.updateUser(user.id, {
          clientId: null,
          userName: userName.trim(),
          email: email.trim(),
          fullName: fullName.trim(),
          phone: normalizedPhone,
          identificationNumber: identificationNumber.trim(),
          password: password || null,
          confirmPassword: password ? confirmPassword : null,
          role: 2,
          isActive: user.isActive
        });
      } else {
        await api.createUser({
          clientId: null,
          userName: userName.trim(),
          email: email.trim(),
          fullName: fullName.trim(),
          phone: normalizedPhone,
          identificationNumber: identificationNumber.trim(),
          password,
          confirmPassword,
          role: 2,
          isActive: true
        });
      }
      Alert.alert("Prestamista guardado", "Los datos se guardaron correctamente.", [
        { text: "Aceptar", onPress: () => navigation.goBack() }
      ]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo guardar el prestamista.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Screen>
      <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
        <ErrorText text={error} />
        <Card title={user ? "Editar prestamista" : "Crear prestamista"}>
          <Field label="Nombre completo" value={fullName} onChangeText={setFullName} placeholder="Nombre y apellidos" />
          <Field label="Usuario o NickName" value={userName} onChangeText={setUserName} autoCapitalize="none" placeholder="Ej. jmedina" />
          <Field label="Correo" value={email} onChangeText={setEmail} autoCapitalize="none" keyboardType="email-address" placeholder="correo@dominio.com" />
          <Field label="Teléfono" value={phone} onChangeText={setPhone} keyboardType="phone-pad" placeholder="88888888" />
          <Field label="Cédula" value={identificationNumber} onChangeText={setIdentificationNumber} autoCapitalize="characters" placeholder="001-010101-0001A" />
          <Field
            label={user ? "Nueva contraseña" : "Contraseña"}
            value={password}
            onChangeText={setPassword}
            secureTextEntry={!showPassword}
            rightActionContent={<Ionicons color={colors.primary} name={showPassword ? "eye-off-outline" : "eye-outline"} size={22} />}
            rightActionAccessibilityLabel={showPassword ? "Ocultar contraseña" : "Mostrar contraseña"}
            onRightAction={() => setShowPassword((value) => !value)}
            placeholder={user ? "Déjala vacía para conservarla" : "Más de 8 caracteres"}
          />
          <Field
            label={user ? "Confirmar nueva contraseña" : "Confirmar contraseña"}
            value={confirmPassword}
            onChangeText={setConfirmPassword}
            secureTextEntry={!showConfirmation}
            rightActionContent={<Ionicons color={colors.primary} name={showConfirmation ? "eye-off-outline" : "eye-outline"} size={22} />}
            rightActionAccessibilityLabel={showConfirmation ? "Ocultar confirmación" : "Mostrar confirmación"}
            onRightAction={() => setShowConfirmation((value) => !value)}
            placeholder={user ? "Repite solo si la cambias" : "Repite la contraseña"}
          />
          <Text style={styles.hint}>
            {user
              ? "Por seguridad no se muestra la contraseña actual. Deja ambos campos vacíos para conservarla o completa los dos para cambiarla."
              : "El prestamista solo verá sus propios clientes, préstamos, pagos, reportes y dashboard."}
          </Text>
          <PrimaryButton title={user ? "Guardar cambios" : "Crear prestamista"} onPress={submit} loading={saving} />
        </Card>
      </ScrollView>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingBottom: spacing.xl },
  hint: { color: colors.muted, fontSize: 12, lineHeight: 18, marginBottom: spacing.md }
});
