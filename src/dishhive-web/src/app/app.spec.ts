import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SwUpdate } from '@angular/service-worker';
import { describe, it, expect, beforeEach } from 'vitest';
import { App } from './app';

describe('App', () => {
  // PwaService skips all service-worker wiring when SwUpdate reports disabled
  const mockSwUpdate = { isEnabled: false };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), { provide: SwUpdate, useValue: mockSwUpdate }]
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it(`should have the 'Dishhive' title`, () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance.title).toBe('Dishhive');
  });
});
