import { useMemo, useState } from "react";
import { Ionicons } from "@expo/vector-icons";
import { Modal, Pressable, StyleSheet, View } from "react-native";
import { colors, spacing } from "../theme/theme";
import { dateOnly } from "../utils/format";
import { Text } from "./ui";

const weekDays = ["L", "M", "M", "J", "V", "S", "D"];

export function DateField({
  label,
  value,
  onChange,
  minimumDate,
  maximumDate
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  minimumDate?: string;
  maximumDate?: string;
}) {
  const selectedDate = parseIsoDate(value) ?? new Date();
  const [visible, setVisible] = useState(false);
  const [visibleMonth, setVisibleMonth] = useState(() => firstOfMonth(selectedDate));
  const calendarDays = useMemo(() => buildCalendarDays(visibleMonth), [visibleMonth]);

  function openCalendar() {
    setVisibleMonth(firstOfMonth(parseIsoDate(value) ?? new Date()));
    setVisible(true);
  }

  function selectDate(date: Date) {
    onChange(toIsoDate(date));
    setVisible(false);
  }

  return (
    <View style={styles.field}>
      <Text style={styles.label}>{label}</Text>
      <Pressable
        accessibilityLabel={`${label}: ${dateOnly(value)}`}
        accessibilityRole="button"
        onPress={openCalendar}
        style={({ pressed }) => [styles.input, pressed && styles.inputPressed]}
      >
        <Text style={styles.value}>{dateOnly(value)}</Text>
        <Ionicons color={colors.primary} name="calendar-outline" size={21} />
      </Pressable>

      <Modal animationType="fade" transparent visible={visible} onRequestClose={() => setVisible(false)}>
        <Pressable onPress={() => setVisible(false)} style={styles.overlay}>
          <Pressable onPress={(event) => event.stopPropagation()} style={styles.panel}>
            <View style={styles.header}>
              <Pressable accessibilityLabel="Mes anterior" hitSlop={8} onPress={() => setVisibleMonth(addMonths(visibleMonth, -1))} style={styles.monthButton}>
                <Ionicons color={colors.primaryDark} name="chevron-back" size={23} />
              </Pressable>
              <Text style={styles.monthTitle}>{monthLabel(visibleMonth)}</Text>
              <Pressable accessibilityLabel="Mes siguiente" hitSlop={8} onPress={() => setVisibleMonth(addMonths(visibleMonth, 1))} style={styles.monthButton}>
                <Ionicons color={colors.primaryDark} name="chevron-forward" size={23} />
              </Pressable>
            </View>

            <View style={styles.weekRow}>
              {weekDays.map((day, index) => <Text key={`${day}-${index}`} style={styles.weekDay}>{day}</Text>)}
            </View>
            <View style={styles.daysGrid}>
              {calendarDays.map((date, index) => {
                if (!date) {
                  return <View key={`empty-${index}`} style={styles.dayCell} />;
                }

                const isoDate = toIsoDate(date);
                const isSelected = isoDate === value;
                const isToday = isoDate === toIsoDate(new Date());
                const disabled = Boolean(
                  minimumDate && isoDate < minimumDate
                  || maximumDate && isoDate > maximumDate
                );

                return (
                  <Pressable
                    accessibilityLabel={dateOnly(isoDate)}
                    accessibilityRole="button"
                    disabled={disabled}
                    key={isoDate}
                    onPress={() => selectDate(date)}
                    style={[styles.dayCell, isSelected && styles.daySelected, isToday && !isSelected && styles.dayToday, disabled && styles.dayDisabled]}
                  >
                    <Text style={[styles.dayText, isSelected && styles.dayTextSelected]}>{date.getDate()}</Text>
                  </Pressable>
                );
              })}
            </View>

            <View style={styles.footer}>
              <Pressable onPress={() => setVisible(false)} style={styles.cancelButton}>
                <Text style={styles.cancelText}>Cancelar</Text>
              </Pressable>
              <Pressable onPress={() => selectDate(new Date())} style={styles.todayButton}>
                <Text style={styles.todayText}>Hoy</Text>
              </Pressable>
            </View>
          </Pressable>
        </Pressable>
      </Modal>
    </View>
  );
}

function parseIsoDate(value: string) {
  const [year, month, day] = value.split("-").map(Number);
  if (!year || !month || !day) return null;
  const date = new Date(year, month - 1, day);
  return Number.isNaN(date.getTime()) ? null : date;
}

function firstOfMonth(date: Date) {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}

function addMonths(date: Date, amount: number) {
  return new Date(date.getFullYear(), date.getMonth() + amount, 1);
}

function toIsoDate(date: Date) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function monthLabel(date: Date) {
  const label = new Intl.DateTimeFormat("es-NI", { month: "long", year: "numeric" }).format(date);
  return label.charAt(0).toUpperCase() + label.slice(1);
}

function buildCalendarDays(month: Date) {
  const firstWeekDay = (month.getDay() + 6) % 7;
  const daysInMonth = new Date(month.getFullYear(), month.getMonth() + 1, 0).getDate();
  const cells: Array<Date | null> = Array.from({ length: firstWeekDay }, () => null);
  for (let day = 1; day <= daysInMonth; day++) {
    cells.push(new Date(month.getFullYear(), month.getMonth(), day));
  }
  while (cells.length % 7 !== 0) cells.push(null);
  return cells;
}

const styles = StyleSheet.create({
  field: { gap: spacing.xs, marginBottom: spacing.sm },
  label: { color: colors.muted, fontSize: 13, fontWeight: "800" },
  input: {
    alignItems: "center",
    backgroundColor: "#fff",
    borderColor: "#bfd1dc",
    borderRadius: 9,
    borderWidth: 1,
    flexDirection: "row",
    justifyContent: "space-between",
    minHeight: 46,
    paddingHorizontal: spacing.md
  },
  inputPressed: { backgroundColor: colors.soft },
  value: { color: colors.text, fontSize: 16 },
  overlay: {
    alignItems: "center",
    backgroundColor: "rgba(16, 34, 50, 0.58)",
    flex: 1,
    justifyContent: "center",
    padding: spacing.lg
  },
  panel: { backgroundColor: "#fff", borderRadius: 12, maxWidth: 420, padding: spacing.md, width: "100%" },
  header: { alignItems: "center", flexDirection: "row", justifyContent: "space-between", marginBottom: spacing.sm },
  monthButton: { alignItems: "center", height: 38, justifyContent: "center", width: 38 },
  monthTitle: { color: colors.text, fontSize: 17, fontWeight: "900" },
  weekRow: { flexDirection: "row", marginBottom: spacing.xs },
  weekDay: { color: colors.muted, flex: 1, fontSize: 12, fontWeight: "900", textAlign: "center" },
  daysGrid: { flexDirection: "row", flexWrap: "wrap" },
  dayCell: { alignItems: "center", aspectRatio: 1, justifyContent: "center", width: "14.2857%" },
  daySelected: { backgroundColor: colors.primary, borderRadius: 999 },
  dayToday: { borderColor: colors.primary, borderRadius: 999, borderWidth: 1 },
  dayDisabled: { opacity: 0.25 },
  dayText: { color: colors.text, fontWeight: "700" },
  dayTextSelected: { color: "#fff", fontWeight: "900" },
  footer: { flexDirection: "row", gap: spacing.sm, justifyContent: "flex-end", marginTop: spacing.md },
  cancelButton: { paddingHorizontal: spacing.md, paddingVertical: spacing.sm },
  cancelText: { color: colors.muted, fontWeight: "800" },
  todayButton: { backgroundColor: colors.primary, borderRadius: 8, paddingHorizontal: spacing.md, paddingVertical: spacing.sm },
  todayText: { color: "#fff", fontWeight: "900" }
});
