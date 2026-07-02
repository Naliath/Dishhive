import { ChangeDetectionStrategy, Component, input, numberAttribute } from '@angular/core';

/**
 * Food-themed loading indicator: a simmering pot with a rattling lid and
 * rising steam. Drop-in replacement for the generic mat-spinner.
 */
@Component({
  selector: 'app-cooking-loader',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './cooking-loader.html',
  styleUrl: './cooking-loader.scss'
})
export class CookingLoaderComponent {
  /** Pixel size of the loader (width and height), like mat-spinner's diameter */
  readonly diameter = input(64, { transform: numberAttribute });
}
