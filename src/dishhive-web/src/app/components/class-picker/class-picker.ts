import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { INGREDIENT_CLASS_GROUPS } from '../../models/ingredient-class.model';
import { TranslatePipe } from '../../services/language.service';

/**
 * Grouped checkbox picker for canonical ingredient classes (EU-14 allergens,
 * meat split, other). Used for a member tag's excluded classes on the family
 * page and a recipe's contained classes on the recipe form.
 */
@Component({
  selector: 'app-class-picker',
  standalone: true,
  imports: [MatCheckboxModule, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './class-picker.html',
  styleUrl: './class-picker.scss'
})
export class ClassPickerComponent {
  readonly selected = input<string[]>([]);
  readonly selectedChange = output<string[]>();

  readonly groups = INGREDIENT_CLASS_GROUPS;

  isChecked(name: string): boolean {
    return this.selected().includes(name);
  }

  toggle(name: string, checked: boolean): void {
    const current = this.selected();
    const next = checked
      ? [...current.filter(c => c !== name), name]
      : current.filter(c => c !== name);
    this.selectedChange.emit(next);
  }
}
