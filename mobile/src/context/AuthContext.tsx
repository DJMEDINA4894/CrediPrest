import type { ReactNode } from "react";
import { createContext, useContext, useEffect, useMemo, useState } from "react";
import * as SecureStore from "expo-secure-store";
import { api, setApiToken } from "../api/client";
import type { LoginResponse } from "../types/models";

const TOKEN_KEY = "crediprest.mobile.token";
const USER_KEY = "crediprest.mobile.user";

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

  useEffect(() => {
    async function restoreSession() {
      const [token, rawUser] = await Promise.all([
        SecureStore.getItemAsync(TOKEN_KEY),
        SecureStore.getItemAsync(USER_KEY)
      ]);

      if (token && rawUser) {
        const savedUser = JSON.parse(rawUser) as LoginResponse;
        setApiToken(token);
        setUser(savedUser);
      }

      setIsReady(true);
    }

    restoreSession().catch(() => setIsReady(true));
  }, []);

  const value = useMemo<AuthContextValue>(() => ({
    user,
    isReady,
    async signIn(userOrEmail, password) {
      const response = await api.login(userOrEmail, password);
      await saveSession(response);
      setUser(response);
    },
    async signInClient(identificationOrPhone) {
      const response = await api.clientLogin(identificationOrPhone);
      await saveSession(response);
      setUser(response);
    },
    async signOut() {
      await Promise.all([
        SecureStore.deleteItemAsync(TOKEN_KEY),
        SecureStore.deleteItemAsync(USER_KEY)
      ]);
      setApiToken(null);
      setUser(null);
    }
  }), [isReady, user]);

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
  setApiToken(response.token);
  await Promise.all([
    SecureStore.setItemAsync(TOKEN_KEY, response.token),
    SecureStore.setItemAsync(USER_KEY, JSON.stringify(response))
  ]);
}
