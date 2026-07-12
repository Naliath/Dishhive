import { Injectable, Pipe, PipeTransform, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { SupportedLanguage } from '../models/user-setting.model';

@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly messages = signal<TranslationTree>({});
  readonly language = signal<SupportedLanguage>('en');
  private loadSequence = 0;

  constructor(private readonly http: HttpClient) {
    // Translations must not depend on the preferences endpoint being available.
    // Preferences can replace this with the user's language once they are loaded.
    this.use('en');
  }

  use(language: SupportedLanguage): void {
    const loadSequence = ++this.loadSequence;
    this.language.set(language);
    document.documentElement.lang = language;
    this.http.get<TranslationTree>(`/i18n/${language}.json`).subscribe({
      next: messages => {
        if (loadSequence === this.loadSequence) {
          this.messages.set(messages);
        }
      },
      error: () => {
        if (loadSequence === this.loadSequence && language !== 'en') {
          this.use('en');
        }
      }
    });
  }

  t(key: string, params: TranslationParams = {}): string {
    const value = this.resolve(key);
    if (typeof value !== 'string') return key;
    return value.replace(/\{(\w+)\}/g, (_, name: string) => String(params[name] ?? `{${name}}`));
  }

  plural(key: string, count: number, params: TranslationParams = {}): string {
    const category = new Intl.PluralRules(this.language()).select(count);
    const value = this.resolve(`${key}.${category}`) ?? this.resolve(`${key}.other`);
    if (typeof value !== 'string') return key;
    return value.replace(/\{(\w+)\}/g, (_, name: string) =>
      String(name === 'count' ? count : params[name] ?? `{${name}}`));
  }

  private resolve(key: string): string | TranslationTree | undefined {
    return key.split('.').reduce<string | TranslationTree | undefined>((current, segment) =>
      current && typeof current === 'object' ? current[segment] : undefined, this.messages());
  }

  /** Localizes computed collection labels while keeping manual collection names verbatim. */
  autoCollectionName(collection: { id: string; name: string }): string {
    switch (collection.id) {
      case 'auto-top-rated': return this.t('collection.topRated');
      case 'auto-quick': return this.t('collection.quickMax30Min');
      case 'auto-recent': return this.t('collection.recentlyAdded');
    }

    if (collection.id.startsWith('auto-fav-')) {
      const suffix = "'s favorites";
      const memberName = collection.name.endsWith(suffix)
        ? collection.name.slice(0, -suffix.length)
        : collection.name;
      return this.t('collection.nameFavorites', { name: memberName });
    }

    return collection.name;
  }
}

@Pipe({ name: 'translate', standalone: true, pure: false })
export class TranslatePipe implements PipeTransform {
  constructor(private readonly language: LanguageService) {}
  transform(key: string, params?: TranslationParams): string { return this.language.t(key, params); }
}

@Pipe({ name: 'translatePlural', standalone: true, pure: false })
export class TranslatePluralPipe implements PipeTransform {
  constructor(private readonly language: LanguageService) {}
  transform(key: string, count: number, params?: TranslationParams): string {
    return this.language.plural(key, count, params);
  }
}

type TranslationTree = { [key: string]: string | TranslationTree };
export type TranslationParams = Record<string, string | number>;

@Pipe({ name: 'autoCollectionName', standalone: true, pure: false })
export class AutoCollectionNamePipe implements PipeTransform {
  constructor(private readonly language: LanguageService) {}
  transform(collection: { id: string; name: string }): string {
    return this.language.autoCollectionName(collection);
  }
}

@Pipe({ name: 'localizedDate', standalone: true, pure: false })
export class LocalizedDatePipe implements PipeTransform {
  constructor(private readonly language: LanguageService) {}

  transform(value: string | Date, format: 'weekday' | 'weekdayShort' | 'weekdayMonth' | 'dayMonth' | 'dayMonthYear' = 'weekday'): string {
    const date = value instanceof Date
      ? value
      : new Date(value.includes('T') ? value : `${value}T00:00:00`);
    const options: Intl.DateTimeFormatOptions = format === 'weekday'
      ? { weekday: 'long' }
      : format === 'weekdayShort'
        ? { weekday: 'long', day: 'numeric', month: 'short' }
        : format === 'weekdayMonth'
          ? { weekday: 'long', day: 'numeric', month: 'long' }
          : format === 'dayMonthYear'
            ? { day: 'numeric', month: 'short', year: 'numeric' }
            : { day: 'numeric', month: 'long' };
    return new Intl.DateTimeFormat(this.language.language(), options).format(date);
  }
}
