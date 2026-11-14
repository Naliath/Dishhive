import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { SettingsService } from '../../services/settings.service';
import { LanguageService, TranslatePipe } from '../../services/language.service';

@Component({
  selector: 'app-cup-conversion-settings',
  imports: [FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule,
    MatInputModule, MatSelectModule, TranslatePipe],
  templateUrl: './cup-conversion-settings.html',
  styleUrl: './cup-conversion-settings.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CupConversionSettingsComponent {
  readonly settings = inject(SettingsService);
  readonly language = inject(LanguageService);
  private readonly snackBar = inject(MatSnackBar);
  readonly editing = signal(false);
  readonly saving = signal(false);
  entries: { ingredient: string; aliases: string; quantity: number | null; unit: string }[] = [];

  edit(): void {
    this.entries = this.settings.cupConversions().map(entry => ({ ...entry, aliases: entry.aliases.join(', ') }));
    this.editing.set(true);
  }

  add(): void {
    this.entries = [...this.entries, { ingredient: '', aliases: '', quantity: null, unit: 'g' }];
  }

  remove(index: number): void {
    this.entries = this.entries.filter((_, position) => position !== index);
  }

  save(): void {
    this.saving.set(true);
    this.settings.setCupConversions(this.entries.map(entry => ({
      ingredient: entry.ingredient.trim(), quantity: entry.quantity!, unit: entry.unit,
      aliases: entry.aliases.split(',').map(alias => alias.trim()).filter(Boolean)
    }))).subscribe({
      next: () => {
        this.saving.set(false);
        this.editing.set(false);
        this.snackBar.open(this.language.t('cupConversions.saved'), this.language.t('common.dismiss'), { duration: 3000 });
      },
      error: () => {
        this.saving.set(false);
        this.snackBar.open(this.language.t('cupConversions.saveError'), this.language.t('common.dismiss'), { duration: 6000 });
      }
    });
  }
}
