import { ChangeDetectionStrategy, Component, OnInit, computed, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TextFieldModule } from '@angular/cdk/text-field';
import { FormsModule } from '@angular/forms';
import { IntegrationsService } from '../../services/integrations.service';
import { SettingsService } from '../../services/settings.service';
import { AiPromptSettings } from '../../models/user-setting.model';

/**
 * Settings card for the editable AI system prompt. Only the persona/preferences
 * section is editable; the machinery the app depends on (JSON contract, collection/
 * source/freezer rules) is shown read-only. Saving a change restarts the model
 * capability test server-side, so the integrations card shows whether the custom
 * prompt still produces working suggestions. Hidden while AI is not configured.
 */
@Component({
  selector: 'app-ai-prompt-settings',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatExpansionModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSnackBarModule,
    TextFieldModule
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ai-prompt-settings.html',
  styleUrl: './ai-prompt-settings.scss'
})
export class AiPromptSettingsComponent implements OnInit {
  readonly maxLength = 4000;

  readonly aiConfigured = signal(false);
  readonly prompt = signal<AiPromptSettings | null>(null);
  readonly saving = signal(false);

  /** The textarea model; compared against the loaded value for dirty state */
  draft = '';

  readonly dirty = computed(() => {
    const loaded = this.prompt();
    return loaded !== null && this.draftTrimmed() !== loaded.editablePrompt;
  });

  private readonly draftValue = signal('');

  constructor(
    private settingsService: SettingsService,
    private integrationsService: IntegrationsService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.integrationsService.getStatus().subscribe(status => {
      this.aiConfigured.set(status?.ai.configured ?? false);
      if (status?.ai.configured) {
        this.settingsService.getAiPrompt().subscribe(prompt => this.apply(prompt));
      }
    });
  }

  onDraftChange(value: string): void {
    this.draft = value;
    this.draftValue.set(value);
  }

  save(): void {
    if (!this.dirty() || this.saving() || this.draftTrimmed().length === 0) {
      return;
    }
    this.saving.set(true);
    this.settingsService.setAiPrompt(this.draftTrimmed()).subscribe(prompt => {
      this.saving.set(false);
      if (!prompt) {
        this.snackBar.open('Could not save the prompt', 'Dismiss', { duration: 4000 });
        return;
      }
      this.apply(prompt);
      this.snackBar.open(
        'Prompt saved — the model capability test restarts so you can see it still works',
        'Dismiss', { duration: 5000 });
    });
  }

  reset(): void {
    if (this.saving()) {
      return;
    }
    this.saving.set(true);
    this.settingsService.resetAiPrompt().subscribe(prompt => {
      this.saving.set(false);
      if (!prompt) {
        this.snackBar.open('Could not reset the prompt', 'Dismiss', { duration: 4000 });
        return;
      }
      this.apply(prompt);
      this.snackBar.open('Prompt reset to the built-in default', 'Dismiss', { duration: 4000 });
    });
  }

  private draftTrimmed(): string {
    return this.draftValue().trim();
  }

  private apply(prompt: AiPromptSettings | null): void {
    this.prompt.set(prompt);
    if (prompt) {
      this.draft = prompt.editablePrompt;
      this.draftValue.set(prompt.editablePrompt);
    }
  }
}
