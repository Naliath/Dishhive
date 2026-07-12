import { Injectable, Pipe, PipeTransform, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { SupportedLanguage } from '../models/user-setting.model';

@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly messages = signal<Record<string, string>>({});
  readonly language = signal<SupportedLanguage>('en');

  constructor(private readonly http: HttpClient) {}

  use(language: SupportedLanguage): void {
    this.language.set(language);
    document.documentElement.lang = language;
    this.http.get<Record<string, string>>(`/i18n/${language}.json`).subscribe({
      next: messages => this.messages.set(messages),
      error: () => language !== 'en' && this.use('en')
    });
  }

  t(key: string): string {
    return this.messages()[key] ?? key;
  }

  /** Localizes computed collection labels while keeping manual collection names verbatim. */
  autoCollectionName(collection: { id: string; name: string }): string {
    switch (collection.id) {
      case 'auto-top-rated': return this.t('Top rated');
      case 'auto-quick': return this.t('Quick (max 30 min)');
      case 'auto-recent': return this.t('Recently added');
    }

    if (collection.id.startsWith('auto-fav-')) {
      const suffix = "'s favorites";
      const memberName = collection.name.endsWith(suffix)
        ? collection.name.slice(0, -suffix.length)
        : collection.name;
      return this.t('{name} favorites').replace('{name}', memberName);
    }

    return collection.name;
  }
}

@Pipe({ name: 'translate', standalone: true, pure: false })
export class TranslatePipe implements PipeTransform {
  constructor(private readonly language: LanguageService) {}
  transform(key: string): string { return this.language.t(key); }
}

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
