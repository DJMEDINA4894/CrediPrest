import { useState } from "react";
import { KeyboardAvoidingView, Platform, StyleSheet, Text, View } from "react-native";
import { StatusBar } from "expo-status-bar";
import { Card, ErrorText, Field, GhostButton, PrimaryButton, Screen } from "../components/ui";
import { useAuth } from "../context/AuthContext";
import { colors, spacing } from "../theme/theme";

export function LoginScreen() {
  const { signIn, signInClient } = useAuth();
  const [mode, setMode] = useState<"staff" | "client">("staff");
  const [userOrEmail, setUserOrEmail] = useState("");
  const [password, setPassword] = useState("");
  const [identificationOrPhone, setIdentificationOrPhone] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  async function submit() {
    try {
      setError("");
      setLoading(true);
      if (mode === "staff") {
        await signIn(userOrEmail, password);
      } else {
        await signInClient(identificationOrPhone);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo iniciar sesion.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <Screen>
      <StatusBar style="dark" />
      <KeyboardAvoidingView behavior={Platform.OS === "ios" ? "padding" : undefined} style={styles.center}>
        <Text style={styles.kicker}>Gestion financiera</Text>
        <Text style={styles.title}>CrediPrest</Text>
        <Text style={styles.subtitle}>Clientes, prestamos, pagos, intereses y mora desde Android.</Text>

        <Card>
          <View style={styles.segment}>
            <GhostButton title="Prestamista" onPress={() => setMode("staff")} />
            <GhostButton title="Cliente" onPress={() => setMode("client")} />
          </View>
          <ErrorText text={error} />
          {mode === "staff" ? (
            <>
              <Field label="Usuario o correo" autoCapitalize="none" value={userOrEmail} onChangeText={setUserOrEmail} />
              <Field label="Contrasena" secureTextEntry value={password} onChangeText={setPassword} />
            </>
          ) : (
            <Field label="Cedula o telefono" value={identificationOrPhone} onChangeText={setIdentificationOrPhone} />
          )}
          <PrimaryButton title="Entrar" onPress={submit} loading={loading} disabled={mode === "staff" ? !userOrEmail || !password : !identificationOrPhone} />
        </Card>

        <Text style={styles.contact}>Contacto: denisjmedinac4894@gmail.com | Claro 58210655 | Tigo 84517258</Text>
      </KeyboardAvoidingView>
    </Screen>
  );
}

const styles = StyleSheet.create({
  center: {
    flex: 1,
    justifyContent: "center"
  },
  kicker: {
    color: colors.primary,
    fontSize: 12,
    fontWeight: "900",
    letterSpacing: 1,
    textTransform: "uppercase"
  },
  title: {
    color: colors.text,
    fontSize: 42,
    fontWeight: "900",
    marginTop: spacing.xs
  },
  subtitle: {
    color: colors.muted,
    fontSize: 16,
    lineHeight: 23,
    marginBottom: spacing.lg,
    marginTop: spacing.sm
  },
  segment: {
    flexDirection: "row",
    gap: spacing.sm,
    marginBottom: spacing.md
  },
  contact: {
    color: colors.muted,
    fontSize: 12,
    lineHeight: 18,
    textAlign: "center"
  }
});
