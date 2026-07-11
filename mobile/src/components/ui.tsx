import type { ReactNode } from "react";
import { useState } from "react";
import { ActivityIndicator, Modal, Pressable, ScrollView, StyleSheet, Text, TextInput, TextInputProps, View } from "react-native";
import { colors, spacing } from "../theme/theme";

export function Screen({ children }: { children: ReactNode }) {
  return <View style={styles.screen}>{children}</View>;
}

export function Card({ title, children }: { title?: string; children: ReactNode }) {
  return (
    <View style={styles.card}>
      {title ? <Text style={styles.cardTitle}>{title}</Text> : null}
      {children}
    </View>
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

export function SecondaryButton({ title, onPress }: { title: string; onPress: () => void }) {
  return (
    <Pressable style={styles.secondaryButton} onPress={onPress}>
      <Text style={styles.secondaryButtonText}>{title}</Text>
    </Pressable>
  );
}

export function Field({ label, ...props }: TextInputProps & { label: string }) {
  return (
    <View style={styles.field}>
      <Text style={styles.label}>{label}</Text>
      <TextInput placeholderTextColor="#8b9ba6" style={styles.input} {...props} />
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
  disabled
}: {
  label: string;
  value: string;
  options: SelectOption[];
  onChange: (value: string) => void;
  placeholder?: string;
  disabled?: boolean;
}) {
  const [visible, setVisible] = useState(false);
  const selectedLabel = options.find((option) => option.value === value)?.label;

  return (
    <View style={styles.field}>
      <Text style={styles.label}>{label}</Text>
      <Pressable disabled={disabled} onPress={() => setVisible(true)} style={[styles.select, disabled && styles.selectDisabled]}>
        <Text numberOfLines={1} style={[styles.selectText, !selectedLabel && styles.selectPlaceholder]}>{selectedLabel ?? placeholder}</Text>
        <Text style={styles.selectChevron}>⌄</Text>
      </Pressable>
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

export function Metric({ title, value, tone }: { title: string; value: string; tone?: "good" | "warn" | "danger" }) {
  return (
    <View style={styles.metric}>
      <Text style={styles.metricTitle}>{title}</Text>
      <Text style={[styles.metricValue, tone ? styles[tone] : undefined]}>{value}</Text>
    </View>
  );
}

export function EmptyState({ text }: { text: string }) {
  return <Text style={styles.empty}>{text}</Text>;
}

export function ErrorText({ text }: { text?: string }) {
  return text ? <Text style={styles.error}>{text}</Text> : null;
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
  cardTitle: {
    color: colors.text,
    fontSize: 16,
    fontWeight: "800",
    marginBottom: spacing.sm
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
  input: {
    backgroundColor: "#fff",
    borderColor: "#bfd1dc",
    borderRadius: 9,
    borderWidth: 1,
    color: colors.text,
    minHeight: 46,
    paddingHorizontal: spacing.md
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
  error: {
    backgroundColor: "#ffe8ea",
    borderColor: "#ef9ea4",
    borderRadius: 9,
    borderWidth: 1,
    color: colors.danger,
    marginBottom: spacing.sm,
    padding: spacing.sm
  }
});
