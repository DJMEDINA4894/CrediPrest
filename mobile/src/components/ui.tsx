import type { ReactNode } from "react";
import { useEffect, useState } from "react";
import { Ionicons } from "@expo/vector-icons";
import type { TextInputProps, TextProps } from "react-native";
import { ActivityIndicator, Modal, Pressable, ScrollView, StyleSheet, Text as NativeText, TextInput, View } from "react-native";
import { usePreferences } from "../context/PreferencesContext";
import { colors, spacing } from "../theme/theme";

export function Text({ style, ...props }: TextProps) {
  const { fontScale } = usePreferences();
  const flattenedStyle = StyleSheet.flatten(style);
  const baseFontSize = typeof flattenedStyle?.fontSize === "number" ? flattenedStyle.fontSize : 14;

  return <NativeText {...props} style={[style, { fontSize: Math.round(baseFontSize * fontScale) }]} />;
}

export function Screen({ children }: { children: ReactNode }) {
  return <View style={styles.screen}>{children}</View>;
}

export function Card({ title, children, tone }: { title?: string; children: ReactNode; tone?: "new" }) {
  return (
    <View style={[styles.card, tone === "new" && styles.cardNew]}>
      {title ? <Text style={styles.cardTitle}>{title}</Text> : null}
      {children}
    </View>
  );
}

export function InfoTooltip({ title = "Información", message }: { title?: string; message: string }) {
  const [visible, setVisible] = useState(false);

  return (
    <>
      <Pressable
        accessibilityLabel={`Ver información: ${title}`}
        accessibilityRole="button"
        hitSlop={8}
        onPress={() => setVisible(true)}
        style={styles.infoButton}
      >
        <Ionicons color="#1769aa" name="information" size={16} />
      </Pressable>
      <Modal animationType="fade" transparent visible={visible} onRequestClose={() => setVisible(false)}>
        <Pressable onPress={() => setVisible(false)} style={styles.infoOverlay}>
          <Pressable onPress={(event) => event.stopPropagation()} style={styles.infoPanel}>
            <View style={styles.infoHeader}>
              <View style={styles.infoTitleRow}>
                <View style={styles.infoTitleIcon}>
                  <Ionicons color="#1769aa" name="information" size={17} />
                </View>
                <Text style={styles.infoTitle}>{title}</Text>
              </View>
              <Pressable accessibilityLabel="Cerrar información" hitSlop={8} onPress={() => setVisible(false)}>
                <Ionicons color={colors.muted} name="close" size={22} />
              </Pressable>
            </View>
            <Text style={styles.infoMessage}>{message}</Text>
          </Pressable>
        </Pressable>
      </Modal>
    </>
  );
}

export function PrimaryButton({
  title,
  onPress,
  disabled,
  loading
}: {
  title: string;
  onPress: () => void;
  disabled?: boolean;
  loading?: boolean;
}) {
  return (
    <Pressable style={[styles.button, disabled && styles.buttonDisabled]} onPress={onPress} disabled={disabled || loading}>
      {loading ? <ActivityIndicator color="#fff" /> : <Text style={styles.buttonText}>{title}</Text>}
    </Pressable>
  );
}

export function GhostButton({ title, onPress }: { title: string; onPress: () => void }) {
  return (
    <Pressable style={styles.ghostButton} onPress={onPress}>
      <Text style={styles.ghostButtonText}>{title}</Text>
    </Pressable>
  );
}

export function DangerButton({ title, onPress }: { title: string; onPress: () => void }) {
  return (
    <Pressable style={styles.dangerButton} onPress={onPress}>
      <Text style={styles.dangerButtonText}>{title}</Text>
    </Pressable>
  );
}

export function SecondaryButton({ title, onPress }: { title: string; onPress: () => void }) {
  return (
    <Pressable style={styles.secondaryButton} onPress={onPress}>
      <Text style={styles.secondaryButtonText}>{title}</Text>
    </Pressable>
  );
}

export function Field({
  label,
  labelAccessory,
  suffix,
  rightActionContent,
  rightActionAccessibilityLabel,
  onRightAction,
  style,
  ...props
}: TextInputProps & {
  label: string;
  labelAccessory?: ReactNode;
  suffix?: string;
  rightActionContent?: ReactNode;
  rightActionAccessibilityLabel?: string;
  onRightAction?: () => void;
}) {
  const { fontScale } = usePreferences();

  return (
    <View style={styles.field}>
      <View style={styles.labelRow}>
        <Text style={styles.label}>{label}</Text>
        {labelAccessory}
      </View>
      <View style={styles.inputWithSuffix}>
        <TextInput
          placeholderTextColor="#8b9ba6"
          style={[styles.input, Boolean(suffix || rightActionContent) && styles.inputWithSuffixValue, { fontSize: Math.round(16 * fontScale) }, style]}
          {...props}
        />
        {suffix ? <Text style={styles.inputSuffix}>{suffix}</Text> : null}
        {rightActionContent && onRightAction ? (
          <Pressable
            accessibilityLabel={rightActionAccessibilityLabel}
            accessibilityRole="button"
            hitSlop={8}
            onPress={onRightAction}
            style={styles.inputAction}
          >
            {rightActionContent}
          </Pressable>
        ) : null}
      </View>
    </View>
  );
}

export type SelectOption = { label: string; value: string };

export function SelectField({
  label,
  value,
  options,
  onChange,
  placeholder = "Selecciona una opción",
  disabled,
  inputAccessory
}: {
  label: string;
  value: string;
  options: SelectOption[];
  onChange: (value: string) => void;
  placeholder?: string;
  disabled?: boolean;
  inputAccessory?: ReactNode;
}) {
  const [visible, setVisible] = useState(false);
  const selectedLabel = options.find((option) => option.value === value)?.label;

  return (
    <View style={styles.field}>
      <Text style={styles.label}>{label}</Text>
      <View style={styles.selectInputRow}>
        <Pressable disabled={disabled} onPress={() => setVisible(true)} style={[styles.select, styles.selectInput, disabled && styles.selectDisabled]}>
          <Text numberOfLines={1} style={[styles.selectText, !selectedLabel && styles.selectPlaceholder]}>{selectedLabel ?? placeholder}</Text>
          <Text style={styles.selectChevron}>⌄</Text>
        </Pressable>
        {inputAccessory}
      </View>
      <Modal animationType="fade" transparent visible={visible} onRequestClose={() => setVisible(false)}>
        <Pressable onPress={() => setVisible(false)} style={styles.modalOverlay}>
          <Pressable onPress={(event) => event.stopPropagation()} style={styles.selectModal}>
            <Text style={styles.selectModalTitle}>{label}</Text>
            <ScrollView>
              {options.map((option) => (
                <Pressable
                  key={option.value}
                  onPress={() => {
                    onChange(option.value);
                    setVisible(false);
                  }}
                  style={[styles.selectOption, option.value === value && styles.selectOptionActive]}
                >
                  <Text style={[styles.selectOptionText, option.value === value && styles.selectOptionTextActive]}>{option.label}</Text>
                </Pressable>
              ))}
            </ScrollView>
          </Pressable>
        </Pressable>
      </Modal>
    </View>
  );
}

export function Metric({ title, value, tone, titleAccessory }: { title: string; value: string; tone?: "good" | "warn" | "danger"; titleAccessory?: ReactNode }) {
  return (
    <View style={styles.metric}>
      <View style={styles.metricTitleRow}>
        <Text style={styles.metricTitle}>{title}</Text>
        {titleAccessory}
      </View>
      <Text style={[styles.metricValue, tone ? styles[tone] : undefined]}>{value}</Text>
    </View>
  );
}

export function EmptyState({ text }: { text: string }) {
  return <Text style={styles.empty}>{text}</Text>;
}

export function ErrorText({ text }: { text?: string }) {
  const [visible, setVisible] = useState(Boolean(text));

  useEffect(() => {
    setVisible(Boolean(text));
  }, [text]);

  if (!text) return null;

  return (
    <Modal animationType="fade" transparent visible={visible} onRequestClose={() => setVisible(false)}>
      <Pressable onPress={() => setVisible(false)} style={styles.errorOverlay}>
        <Pressable onPress={(event) => event.stopPropagation()} style={styles.errorPanel}>
          <View style={styles.errorIcon}>
            <Ionicons color="#fff" name="alert" size={22} />
          </View>
          <Text style={styles.errorTitle}>No se pudo completar la acción</Text>
          <Text style={styles.errorMessage}>{text}</Text>
          <Pressable accessibilityRole="button" onPress={() => setVisible(false)} style={styles.errorButton}>
            <Text style={styles.errorButtonText}>Entendido</Text>
          </Pressable>
        </Pressable>
      </Pressable>
    </Modal>
  );
}

const styles = StyleSheet.create({
  screen: {
    flex: 1,
    backgroundColor: colors.background,
    padding: spacing.md
  },
  card: {
    backgroundColor: colors.card,
    borderColor: colors.border,
    borderRadius: 10,
    borderWidth: 1,
    marginBottom: spacing.md,
    padding: spacing.md
  },
  cardNew: {
    backgroundColor: "#fff8e8",
    borderColor: "#efc778"
  },
  cardTitle: {
    color: colors.text,
    fontSize: 16,
    fontWeight: "800",
    marginBottom: spacing.sm
  },
  infoButton: {
    alignItems: "center",
    backgroundColor: "#e7f2fb",
    borderColor: "#77add4",
    borderRadius: 999,
    borderWidth: 1,
    height: 22,
    justifyContent: "center",
    width: 22
  },
  infoOverlay: {
    alignItems: "center",
    backgroundColor: "rgba(16, 34, 50, 0.45)",
    flex: 1,
    justifyContent: "center",
    padding: spacing.lg
  },
  infoPanel: {
    backgroundColor: "#fff",
    borderColor: "#9ac2df",
    borderRadius: 10,
    borderWidth: 1,
    maxWidth: 440,
    padding: spacing.md,
    width: "100%"
  },
  infoHeader: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
    marginBottom: spacing.sm
  },
  infoTitleRow: {
    alignItems: "center",
    flexDirection: "row",
    flex: 1,
    gap: spacing.sm
  },
  infoTitleIcon: {
    alignItems: "center",
    backgroundColor: "#e7f2fb",
    borderRadius: 999,
    height: 24,
    justifyContent: "center",
    width: 24
  },
  infoTitle: {
    color: colors.text,
    fontSize: 16,
    fontWeight: "900"
  },
  infoMessage: {
    color: colors.text,
    lineHeight: 21
  },
  button: {
    alignItems: "center",
    backgroundColor: colors.primary,
    borderRadius: 9,
    minHeight: 46,
    justifyContent: "center",
    paddingHorizontal: spacing.md
  },
  buttonDisabled: {
    opacity: 0.7
  },
  buttonText: {
    color: "#fff",
    fontWeight: "800"
  },
  ghostButton: {
    alignItems: "center",
    backgroundColor: colors.soft,
    borderRadius: 9,
    minHeight: 42,
    justifyContent: "center",
    paddingHorizontal: spacing.md
  },
  ghostButtonText: {
    color: colors.primaryDark,
    fontWeight: "800"
  },
  dangerButton: {
    alignItems: "center",
    backgroundColor: "#fff0f1",
    borderColor: "#e7a5aa",
    borderRadius: 9,
    borderWidth: 1,
    minHeight: 42,
    justifyContent: "center",
    paddingHorizontal: spacing.md
  },
  dangerButtonText: {
    color: colors.danger,
    fontWeight: "900"
  },
  secondaryButton: {
    alignItems: "center",
    borderColor: colors.primary,
    borderRadius: 9,
    borderWidth: 1,
    minHeight: 42,
    justifyContent: "center",
    paddingHorizontal: spacing.md
  },
  secondaryButtonText: {
    color: colors.primaryDark,
    fontWeight: "800"
  },
  select: {
    alignItems: "center",
    backgroundColor: "#fff",
    borderColor: "#bfd1dc",
    borderRadius: 9,
    borderWidth: 1,
    flexDirection: "row",
    minHeight: 46,
    paddingHorizontal: spacing.md
  },
  selectInputRow: {
    alignItems: "center",
    flexDirection: "row",
    gap: spacing.sm
  },
  selectInput: {
    flex: 1
  },
  selectDisabled: {
    backgroundColor: "#edf1f3",
    opacity: 0.7
  },
  selectText: {
    color: colors.text,
    flex: 1
  },
  selectPlaceholder: {
    color: "#8b9ba6"
  },
  selectChevron: {
    color: colors.primaryDark,
    fontSize: 20,
    marginLeft: spacing.sm
  },
  modalOverlay: {
    alignItems: "center",
    backgroundColor: "rgba(16, 34, 50, 0.45)",
    flex: 1,
    justifyContent: "center",
    padding: spacing.lg
  },
  selectModal: {
    backgroundColor: "#fff",
    borderRadius: 12,
    maxHeight: "72%",
    padding: spacing.md,
    width: "100%"
  },
  selectModalTitle: {
    color: colors.text,
    fontSize: 17,
    fontWeight: "900",
    marginBottom: spacing.sm
  },
  selectOption: {
    borderBottomColor: colors.border,
    borderBottomWidth: 1,
    paddingVertical: spacing.md
  },
  selectOptionActive: {
    backgroundColor: colors.soft,
    borderRadius: 8,
    paddingHorizontal: spacing.sm
  },
  selectOptionText: {
    color: colors.text,
    fontWeight: "700"
  },
  selectOptionTextActive: {
    color: colors.primaryDark,
    fontWeight: "900"
  },
  field: {
    gap: spacing.xs,
    marginBottom: spacing.sm
  },
  label: {
    color: colors.muted,
    fontSize: 13,
    fontWeight: "800"
  },
  labelRow: {
    alignItems: "center",
    flexDirection: "row",
    gap: spacing.xs
  },
  input: {
    backgroundColor: "#fff",
    borderColor: "#bfd1dc",
    borderRadius: 9,
    borderWidth: 1,
    color: colors.text,
    minHeight: 46,
    paddingHorizontal: spacing.md
  },
  inputWithSuffix: {
    position: "relative"
  },
  inputWithSuffixValue: {
    paddingRight: 78
  },
  inputSuffix: {
    color: colors.muted,
    fontWeight: "900",
    position: "absolute",
    right: 14,
    top: 13
  },
  inputAction: {
    alignItems: "center",
    bottom: 1,
    justifyContent: "center",
    paddingHorizontal: 12,
    position: "absolute",
    right: 1,
    top: 1
  },
  metric: {
    backgroundColor: "#fff",
    borderColor: colors.border,
    borderRadius: 10,
    borderWidth: 1,
    flex: 1,
    minWidth: "47%",
    padding: spacing.md
  },
  metricTitle: {
    color: colors.muted,
    fontSize: 12,
    fontWeight: "700",
    marginBottom: spacing.xs
  },
  metricTitleRow: {
    alignItems: "center",
    flexDirection: "row",
    gap: 4
  },
  metricValue: {
    color: colors.text,
    fontSize: 19,
    fontWeight: "900"
  },
  good: {
    color: colors.good
  },
  warn: {
    color: colors.warn
  },
  danger: {
    color: colors.danger
  },
  empty: {
    color: colors.muted,
    fontWeight: "700",
    paddingVertical: spacing.md,
    textAlign: "center"
  },
  errorOverlay: {
    alignItems: "center",
    backgroundColor: "rgba(16, 34, 50, 0.62)",
    flex: 1,
    justifyContent: "center",
    padding: spacing.lg
  },
  errorPanel: {
    alignItems: "center",
    backgroundColor: "#fff",
    borderColor: "#ef9ea4",
    borderRadius: 12,
    borderWidth: 1,
    maxWidth: 430,
    padding: spacing.lg,
    width: "100%"
  },
  errorIcon: {
    alignItems: "center",
    backgroundColor: colors.danger,
    borderRadius: 999,
    height: 44,
    justifyContent: "center",
    marginBottom: spacing.sm,
    width: 44
  },
  errorTitle: {
    color: colors.text,
    fontSize: 17,
    fontWeight: "900",
    marginBottom: spacing.sm,
    textAlign: "center"
  },
  errorMessage: {
    color: colors.muted,
    lineHeight: 21,
    marginBottom: spacing.lg,
    textAlign: "center"
  },
  errorButton: {
    alignItems: "center",
    backgroundColor: colors.primary,
    borderRadius: 9,
    minHeight: 44,
    justifyContent: "center",
    paddingHorizontal: spacing.xl,
    width: "100%"
  },
  errorButtonText: { color: "#fff", fontWeight: "900" }
});
