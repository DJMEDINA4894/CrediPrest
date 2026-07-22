import { useEffect, useState } from "react";
import { Ionicons } from "@expo/vector-icons";
import { KeyboardAvoidingView, Modal, Platform, Pressable, StyleSheet, TextInput, View } from "react-native";
import { api } from "../api/client";
import { colors, spacing } from "../theme/theme";
import type { CurrencyType, ExchangeRate } from "../types/models";
import { currencyLabels, money } from "../utils/format";
import { Card, PrimaryButton, Text } from "./ui";

export function ExchangeRateCalculatorCard() {
  const [exchangeRate, setExchangeRate] = useState<ExchangeRate | null>(null);
  const [visible, setVisible] = useState(false);
  const [sourceCurrency, setSourceCurrency] = useState<CurrencyType>(1);
  const [amount, setAmount] = useState("0");

  useEffect(() => {
    let active = true;
    api.currentExchangeRate()
      .then((rate) => {
        if (active) setExchangeRate(rate);
      })
      .catch(() => {
        // La calculadora se oculta si la tasa del día no está disponible.
      });

    return () => {
      active = false;
    };
  }, []);

  if (!exchangeRate) {
    return null;
  }

  const targetCurrency: CurrencyType = sourceCurrency === 1 ? 2 : 1;
  const numericAmount = Number(amount.replace(",", "."));
  const appliedRate = sourceCurrency === 2
    ? exchangeRate.buyCordobasPerUsd
    : exchangeRate.sellCordobasPerUsd;
  const convertedAmount = Number.isFinite(numericAmount) && numericAmount >= 0
    ? sourceCurrency === 2
      ? numericAmount * appliedRate
      : numericAmount / appliedRate
    : 0;

  function swapCurrencies() {
    setSourceCurrency(targetCurrency);
    setAmount(convertedAmount > 0 ? convertedAmount.toFixed(2) : "0");
  }

  return (
    <>
      <Card title="Tipo de cambio">
        <View style={styles.quoteRow}>
          <View style={styles.quoteItem}>
            <Text style={styles.quoteLabel}>Compra</Text>
            <Text style={styles.quoteValue}>C$ {exchangeRate.buyCordobasPerUsd.toFixed(2)}</Text>
          </View>
          <View style={styles.quoteItem}>
            <Text style={styles.quoteLabel}>Venta</Text>
            <Text style={styles.quoteValue}>C$ {exchangeRate.sellCordobasPerUsd.toFixed(2)}</Text>
          </View>
        </View>
        <PrimaryButton title="Calcular cambio" onPress={() => setVisible(true)} />
      </Card>

      <Modal animationType="fade" transparent visible={visible} onRequestClose={() => setVisible(false)}>
        <Pressable style={styles.overlay} onPress={() => setVisible(false)}>
          <KeyboardAvoidingView behavior={Platform.OS === "ios" ? "padding" : undefined} style={styles.keyboardArea}>
            <Pressable style={styles.modal} onPress={(event) => event.stopPropagation()}>
              <View style={styles.header}>
                <View>
                  <Text style={styles.kicker}>Conversión informativa</Text>
                  <Text style={styles.title}>Tipo de cambio</Text>
                </View>
                <Pressable accessibilityLabel="Cerrar calculadora" hitSlop={8} onPress={() => setVisible(false)} style={styles.closeButton}>
                  <Ionicons color={colors.muted} name="close" size={24} />
                </Pressable>
              </View>

              <View style={styles.currencyRow}>
                <Pressable accessibilityRole="button" onPress={swapCurrencies} style={styles.currencyButton}>
                  <Text style={styles.currencyCode}>{currencyLabels[sourceCurrency]}</Text>
                  <Text style={styles.currencyName}>{sourceCurrency === 1 ? "Córdobas" : "Dólares"}</Text>
                </Pressable>
                <Pressable accessibilityLabel="Intercambiar monedas" accessibilityRole="button" onPress={swapCurrencies} style={styles.swapButton}>
                  <Ionicons color="#fff" name="swap-horizontal" size={22} />
                </Pressable>
                <Pressable accessibilityRole="button" onPress={swapCurrencies} style={styles.currencyButton}>
                  <Text style={styles.currencyCode}>{currencyLabels[targetCurrency]}</Text>
                  <Text style={styles.currencyName}>{targetCurrency === 1 ? "Córdobas" : "Dólares"}</Text>
                </Pressable>
              </View>

              <Text style={styles.inputLabel}>Monto en {currencyLabels[sourceCurrency]}</Text>
              <TextInput
                accessibilityLabel={`Monto en ${currencyLabels[sourceCurrency]}`}
                keyboardType="decimal-pad"
                onChangeText={setAmount}
                onFocus={() => amount === "0" && setAmount("")}
                placeholder="0.00"
                placeholderTextColor="#8b9ba6"
                selectTextOnFocus
                style={styles.input}
                value={amount}
              />

              <View style={styles.result}>
                <Text style={styles.resultLabel}>Resultado</Text>
                <Text style={styles.resultValue}>{money(convertedAmount, currencyLabels[targetCurrency])}</Text>
              </View>
              <Text style={styles.note}>
                {sourceCurrency === 2 ? "Se aplica la tasa de compra." : "Se aplica la tasa de venta."} Este cálculo no modifica préstamos ni pagos.
              </Text>
            </Pressable>
          </KeyboardAvoidingView>
        </Pressable>
      </Modal>
    </>
  );
}

const styles = StyleSheet.create({
  quoteRow: {
    flexDirection: "row",
    gap: spacing.sm,
    marginBottom: spacing.md
  },
  quoteItem: {
    backgroundColor: colors.soft,
    borderRadius: 8,
    flex: 1,
    padding: spacing.sm
  },
  quoteLabel: {
    color: colors.muted,
    fontSize: 11,
    fontWeight: "900",
    textTransform: "uppercase"
  },
  quoteValue: {
    color: colors.primaryDark,
    fontSize: 17,
    fontWeight: "900",
    marginTop: 2
  },
  overlay: {
    backgroundColor: "rgba(16, 34, 50, 0.55)",
    flex: 1,
    justifyContent: "center",
    padding: spacing.md
  },
  keyboardArea: {
    justifyContent: "center"
  },
  modal: {
    alignSelf: "center",
    backgroundColor: "#fff",
    borderColor: colors.border,
    borderRadius: 12,
    borderWidth: 1,
    maxWidth: 480,
    padding: spacing.lg,
    width: "100%"
  },
  header: {
    alignItems: "flex-start",
    flexDirection: "row",
    justifyContent: "space-between",
    marginBottom: spacing.lg
  },
  kicker: {
    color: colors.primary,
    fontSize: 11,
    fontWeight: "900",
    textTransform: "uppercase"
  },
  title: {
    color: colors.text,
    fontSize: 22,
    fontWeight: "900",
    marginTop: 3
  },
  closeButton: {
    alignItems: "center",
    backgroundColor: colors.soft,
    borderRadius: 999,
    height: 36,
    justifyContent: "center",
    width: 36
  },
  currencyRow: {
    alignItems: "center",
    flexDirection: "row",
    gap: spacing.sm,
    marginBottom: spacing.lg
  },
  currencyButton: {
    backgroundColor: colors.soft,
    borderColor: colors.border,
    borderRadius: 8,
    borderWidth: 1,
    flex: 1,
    padding: spacing.sm
  },
  currencyCode: {
    color: colors.primaryDark,
    fontSize: 17,
    fontWeight: "900"
  },
  currencyName: {
    color: colors.muted,
    fontSize: 11,
    marginTop: 2
  },
  swapButton: {
    alignItems: "center",
    backgroundColor: colors.primary,
    borderRadius: 999,
    height: 42,
    justifyContent: "center",
    width: 42
  },
  inputLabel: {
    color: colors.text,
    fontWeight: "800",
    marginBottom: spacing.xs
  },
  input: {
    backgroundColor: "#fff",
    borderColor: colors.border,
    borderRadius: 8,
    borderWidth: 1,
    color: colors.text,
    fontSize: 18,
    fontWeight: "800",
    marginBottom: spacing.md,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm
  },
  result: {
    backgroundColor: "#e8f8f5",
    borderColor: "#76c9bd",
    borderRadius: 8,
    borderWidth: 1,
    padding: spacing.md
  },
  resultLabel: {
    color: colors.muted,
    fontSize: 11,
    fontWeight: "900",
    textTransform: "uppercase"
  },
  resultValue: {
    color: colors.primary,
    fontSize: 24,
    fontWeight: "900",
    marginTop: 3
  },
  note: {
    color: colors.muted,
    fontSize: 12,
    lineHeight: 18,
    marginTop: spacing.sm
  }
});
