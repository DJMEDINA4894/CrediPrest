import { useState } from "react";
import { KeyboardAvoidingView, Platform, Pressable, ScrollView, StyleSheet, View } from "react-native";
import { StatusBar } from "expo-status-bar";
import { Ionicons } from "@expo/vector-icons";
import { Card, ErrorText, Field, PrimaryButton, Screen, Text } from "../components/ui";
import { useAuth } from "../context/AuthContext";
import { colors, spacing } from "../theme/theme";

export function LoginScreen() {
  const { signIn, signInClient } = useAuth();
  const [mode, setMode] = useState<"staff" | "client">("staff");
  const [userOrEmail, setUserOrEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
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
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          <Text style={styles.kicker}>Gestión financiera</Text>
          <Text style={styles.title}>CrediPrest</Text>
          <Text style={styles.subtitle}>Clientes, préstamos, pagos, intereses y mora desde Android.</Text>

          <Card>
            <View style={styles.segment}>
              <Pressable accessibilityRole="button" onPress={() => setMode("staff")} style={[styles.segmentButton, mode === "staff" && styles.segmentButtonActive]}>
                <Text style={[styles.segmentText, mode === "staff" && styles.segmentTextActive]}>Prestamista</Text>
              </Pressable>
              <Pressable accessibilityRole="button" onPress={() => setMode("client")} style={[styles.segmentButton, mode === "client" && styles.segmentButtonActive]}>
                <Text style={[styles.segmentText, mode === "client" && styles.segmentTextActive]}>Cliente</Text>
              </Pressable>
            </View>
            <ErrorText text={error} />
            {mode === "staff" ? (
              <>
                <Field label="Usuario o correo" autoCapitalize="none" value={userOrEmail} onChangeText={setUserOrEmail} />
                <Field
                  label="Contraseña"
                  secureTextEntry={!showPassword}
                  value={password}
                  onChangeText={setPassword}
                  rightActionContent={<Ionicons color={colors.primary} name={showPassword ? "eye-off-outline" : "eye-outline"} size={22} />}
                  rightActionAccessibilityLabel={showPassword ? "Ocultar contraseña" : "Mostrar contraseña"}
                  onRightAction={() => setShowPassword((value) => !value)}
                />
              </>
            ) : (
              <Field label="Cédula o teléfono" value={identificationOrPhone} onChangeText={setIdentificationOrPhone} />
            )}
            <PrimaryButton title="Entrar" onPress={submit} loading={loading} disabled={mode === "staff" ? !userOrEmail || !password : !identificationOrPhone} />
          </Card>

          <Card title="Privacidad y acceso">
            <Text style={styles.security}>Cada cliente solo puede consultar sus propios préstamos. Los prestamistas requieren una cuenta creada por el administrador.</Text>
          </Card>

          <Card title="Contacto directo">
            <View style={styles.contactRow}><Text style={styles.contactLabel}>Correo</Text><Text style={styles.contactValue}>denisjmedinac4894@gmail.com</Text></View>
            <View style={styles.contactRow}><Text style={styles.contactLabel}>WhatsApp / Claro</Text><Text style={styles.contactValue}>58210655</Text></View>
            <View style={styles.contactRow}><Text style={styles.contactLabel}>Línea Tigo</Text><Text style={styles.contactValue}>84517258</Text></View>
          </Card>
        </ScrollView>
      </KeyboardAvoidingView>
    </Screen>
  );
}

const styles = StyleSheet.create({
  center: {
    flex: 1
  },
  content: {
    flexGrow: 1,
    paddingBottom: spacing.xl,
    paddingTop: spacing.xl
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
    backgroundColor: colors.soft,
    borderRadius: 9,
    flexDirection: "row",
    marginBottom: spacing.md,
    padding: 4
  },
  segmentButton: {
    alignItems: "center",
    borderRadius: 7,
    flex: 1,
    minHeight: 40,
    justifyContent: "center"
  },
  segmentButtonActive: {
    backgroundColor: colors.primary
  },
  segmentText: {
    color: colors.primaryDark,
    fontWeight: "900"
  },
  segmentTextActive: {
    color: "#fff"
  },
  security: {
    color: colors.muted,
    lineHeight: 21
  },
  contactRow: {
    borderBottomColor: colors.border,
    borderBottomWidth: 1,
    paddingVertical: spacing.sm
  },
  contactLabel: {
    color: colors.muted,
    fontSize: 11,
    fontWeight: "900",
    textTransform: "uppercase"
  },
  contactValue: {
    color: colors.text,
    fontWeight: "800",
    marginTop: 3
  }
});
