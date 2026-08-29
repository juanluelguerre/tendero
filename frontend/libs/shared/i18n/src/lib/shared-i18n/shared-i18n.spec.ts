import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SharedI18n } from './shared-i18n';

describe('SharedI18n', () => {
  let component: SharedI18n;
  let fixture: ComponentFixture<SharedI18n>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SharedI18n],
    }).compileComponents();

    fixture = TestBed.createComponent(SharedI18n);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
