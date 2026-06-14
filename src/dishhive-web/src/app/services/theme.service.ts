import { Injectable, signal, effect } from '@angular/core';

export type ThemeMode = 'auto' | 'light' | 'dark';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly STORAGE_KEY = 'dishhive-theme';

  readonly currentTheme = signal<ThemeMode>('auto');
  readonly effectiveTheme = signal<'light' | 'dark'>('light');

  constructor() {
    this.loadSavedTheme();
    this.setupSystemThemeListener();
    effect(() => this.applyTheme(this.currentTheme()));
  }

  setTheme(mode: ThemeMode): void {
    this.currentTheme.set(mode);
    localStorage.setItem(this.STORAGE_KEY, mode);
  }

  private loadSavedTheme(): void {
    const saved = localStorage.getItem(this.STORAGE_KEY) as ThemeMode;
    if (saved && ['auto', 'light', 'dark'].includes(saved)) {
      this.currentTheme.set(saved);
    }
  }

  private applyTheme(mode: ThemeMode): void {
    const html = document.documentElement;
    html.classList.remove('light-mode', 'dark-mode');

    if (mode === 'light') {
      html.classList.add('light-mode');
      this.effectiveTheme.set('light');
    } else if (mode === 'dark') {
      html.classList.add('dark-mode');
      this.effectiveTheme.set('dark');
    } else {
      // Auto: no class — CSS color-scheme: light dark handles rendering.
      // We only update effectiveTheme to reflect the system state in the UI.
      const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
      this.effectiveTheme.set(prefersDark ? 'dark' : 'light');
    }
  }

  private setupSystemThemeListener(): void {
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', e => {
      if (this.currentTheme() === 'auto') {
        this.effectiveTheme.set(e.matches ? 'dark' : 'light');
      }
    });
  }
}
