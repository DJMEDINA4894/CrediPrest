import type { ReactNode } from "react";
import { createContext, useContext, useEffect, useMemo, useState } from "react";
import * as SecureStore from "expo-secure-store";

const FONT_SIZE_KEY = "crediprest.mobile.fontSize";
export const DEFAULT_FONT_SIZE = 16;

type PreferencesContextValue = {
  fontSize: number;
  fontScale: number;
  setFontSize: (value: number) => void;
};

const PreferencesContext = createContext<PreferencesContextValue | null>(null);

export function PreferencesProvider({ children }: { children: ReactNode }) {
  const [fontSize, updateFontSize] = useState(DEFAULT_FONT_SIZE);

  useEffect(() => {
    SecureStore.getItemAsync(FONT_SIZE_KEY)
      .then((storedValue) => {
        const value = Number(storedValue);
        if (Number.isFinite(value) && value >= 14 && value <= 20) {
          updateFontSize(value);
        }
      })
      .catch(() => undefined);
  }, []);

  const value = useMemo<PreferencesContextValue>(() => ({
    fontSize,
    fontScale: fontSize / DEFAULT_FONT_SIZE,
    setFontSize(nextValue) {
      const normalizedValue = Math.min(20, Math.max(14, Math.round(nextValue)));
      updateFontSize(normalizedValue);
      SecureStore.setItemAsync(FONT_SIZE_KEY, String(normalizedValue)).catch(() => undefined);
    }
  }), [fontSize]);

  return <PreferencesContext.Provider value={value}>{children}</PreferencesContext.Provider>;
}

export function usePreferences() {
  const value = useContext(PreferencesContext);
  if (!value) {
    throw new Error("usePreferences debe usarse dentro de PreferencesProvider.");
  }

  return value;
}
