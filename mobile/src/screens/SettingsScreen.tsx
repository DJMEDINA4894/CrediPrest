import { StyleSheet, View } from "react-native";
import { Card, GhostButton, Screen, Text } from "../components/ui";
import { DEFAULT_FONT_SIZE, usePreferences } from "../context/PreferencesContext";
import { colors, spacing } from "../theme/theme";

const sizes = [14, 16, 18, 20];

export function SettingsScreen() {
  const { fontSize, setFontSize } = usePreferences();

  return (
    <Screen>
      <Card title="Preferencias de interfaz">
        <Text style={styles.label}>Tamaño de letra</Text>
        <Text style={styles.copy}>Ajusta el texto general para leer cómodamente formularios, tarjetas y tablas de pago.</Text>
        <View style={styles.options}>
          {sizes.map((size) => (
            <GhostButton key={size} title={`${size}px${fontSize === size ? " · Actual" : ""}`} onPress={() => setFontSize(size)} />
          ))}
        </View>
        <View style={styles.preview}>
          <Text style={styles.previewTitle}>CrediPrest</Text>
          <Text style={styles.copy}>Vista previa de la información financiera con el tamaño seleccionado.</Text>
        </View>
        {fontSize !== DEFAULT_FONT_SIZE ? <GhostButton title="Restablecer tamaño" onPress={() => setFontSize(DEFAULT_FONT_SIZE)} /> : null}
      </Card>
    </Screen>
  );
}

const styles = StyleSheet.create({
  label: { color: colors.text, fontWeight: "900" },
  copy: { color: colors.muted, lineHeight: 21, marginTop: spacing.xs },
  options: { flexDirection: "row", flexWrap: "wrap", gap: spacing.sm, marginVertical: spacing.md },
  preview: { backgroundColor: colors.soft, borderRadius: 8, marginBottom: spacing.md, padding: spacing.md },
  previewTitle: { color: colors.text, fontSize: 20, fontWeight: "900" }
});
