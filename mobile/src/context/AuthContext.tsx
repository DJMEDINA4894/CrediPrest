import type { ReactNode } from "react";
import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import * as SecureStore from "expo-secure-store";
import { AppState } from "react-native";
import { api, setApiToken } from "../api/client";
import type { LoginResponse } from "../types/models";

const TOKEN_KEY = "crediprest.mobile.token";
const USER_KEY = "crediprest.mobile.user";
const SESSION_STARTED_AT_KEY = "crediprest.mobile.session.startedAt";
const SESSION_DURATION_MS = 8 * 60 * 60 * 1000;

type AuthContextValue = {
  user: LoginResponse | null;
  isReady: boolean;
  signIn: (userOrEmail: string, password: string) => Promise<void>;
  signInClient: (identificationOrPhone: string) => Promise<void>;
  signOut: () => Promise<void>;
};

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<LoginResponse | null>(null);
  const [isReady, setIsReady] = useState(false);
  const [sessionExpiresAt, setSessionExpiresAt] = useState<number | null>(null);

  const clearSession = useCallback(async () => {
    await Promise.all([
      SecureStore.deleteItemAsync(TOKEN_KEY),
      SecureStore.deleteItemAsync(USER_KEY),
      SecureStore.deleteItemAsync(SESSION_STARTED_AT_KEY)
    ]);
    setApiToken(null);
    setUser(null);
    setSessionExpiresAt(null);
  }, []);

  useEffect(() => {
    async function restoreSession() {
      const [token, rawUser, rawStartedAt] = await Promise.all([
        SecureStore.getItemAsync(TOKEN_KEY),
        SecureStore.getItemAsync(USER_KEY),
        SecureStore.getItemAsync(SESSION_STARTED_AT_KEY)
      ]);
      const startedAt = Number(rawStartedAt);
      const expiresAt = startedAt + SESSION_DURATION_MS;

      if (token && rawUser && Number.isFinite(startedAt) && startedAt > 0 && expiresAt > Date.now()) {
        const savedUser = JSON.parse(rawUser) as LoginResponse;
        setApiToken(token);
        setUser(savedUser);
        setSessionExpiresAt(expiresAt);
      } else {
        await clearSession();
      }
    }

    restoreSession()
      .catch(() => clearSession())
      .finally(() => setIsReady(true));
  }, [clearSession]);

  useEffect(() => {
    if (!user || !sessionExpiresAt) {
      return;
    }

    const expireIfNeeded = () => {
      if (Date.now() >= sessionExpiresAt) {
        void clearSession();
      }
    };
    const timeoutId = setTimeout(expireIfNeeded, Math.max(0, sessionExpiresAt - Date.now()));
    const subscription = AppState.addEventListener("change", (state) => {
      if (state === "active") {
        expireIfNeeded();
      }
    });

    return () => {
      clearTimeout(timeoutId);
      subscription.remove();
    };
  }, [clearSession, sessionExpiresAt, user]);

  const value = useMemo<AuthContextValue>(() => ({
    user,
    isReady,
    async signIn(userOrEmail, password) {
      const response = await api.login(userOrEmail, password);
      setSessionExpiresAt(await saveSession(response));
      setUser(response);
    },
    async signInClient(identificationOrPhone) {
      const response = await api.clientLogin(identificationOrPhone);
      setSessionExpiresAt(await saveSession(response));
      setUser(response);
    },
    async signOut() {
      await clearSession();
    }
  }), [clearSession, isReady, user]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const value = useContext(AuthContext);
  if (!value) {
    throw new Error("useAuth debe usarse dentro de AuthProvider.");
  }

  return value;
}

async function saveSession(response: LoginResponse) {
  const startedAt = Date.now();
  setApiToken(response.token);
  await Promise.all([
    SecureStore.setItemAsync(TOKEN_KEY, response.token),
    SecureStore.setItemAsync(USER_KEY, JSON.stringify(response)),
    SecureStore.setItemAsync(SESSION_STARTED_AT_KEY, String(startedAt))
  ]);
  return startedAt + SESSION_DURATION_MS;
}
