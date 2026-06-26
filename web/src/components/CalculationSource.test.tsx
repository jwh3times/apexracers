import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import CalculationSource, { type PaceSourceValue } from './CalculationSource';

function renderWith(value: PaceSourceValue, onChange = vi.fn()) {
  return { onChange, ...render(<CalculationSource value={value} onChange={onChange} />) };
}

describe('CalculationSource', () => {
  it('renders both mode tiles', () => {
    renderWith({ mode: 'official', sessions: [] });
    expect(screen.getByText('Official race laps only')).toBeInTheDocument();
    expect(screen.getByText('Official + my uploaded laps')).toBeInTheDocument();
  });

  it('official tile is aria-checked when mode is official', () => {
    renderWith({ mode: 'official', sessions: [] });
    expect(screen.getByRole('radio', { name: /official race laps only/i })).toHaveAttribute(
      'aria-checked',
      'true'
    );
    expect(screen.getByRole('radio', { name: /official \+ my uploaded laps/i })).toHaveAttribute(
      'aria-checked',
      'false'
    );
  });

  it('blend tile is aria-checked when mode is blend', () => {
    renderWith({ mode: 'blend', sessions: [] });
    expect(screen.getByRole('radio', { name: /official \+ my uploaded laps/i })).toHaveAttribute(
      'aria-checked',
      'true'
    );
    expect(screen.getByRole('radio', { name: /official race laps only/i })).toHaveAttribute(
      'aria-checked',
      'false'
    );
  });

  it('clicking blend tile calls onChange with mode blend', () => {
    const { onChange } = renderWith({ mode: 'official', sessions: [] });
    fireEvent.click(screen.getByRole('radio', { name: /official \+ my uploaded laps/i }));
    expect(onChange).toHaveBeenCalledWith({ mode: 'blend', sessions: [] });
  });

  it('clicking official tile when already official does not call onChange', () => {
    const { onChange } = renderWith({ mode: 'official', sessions: [] });
    fireEvent.click(screen.getByRole('radio', { name: /official race laps only/i }));
    expect(onChange).not.toHaveBeenCalled();
  });

  it('switching back to official clears sessions in emitted value', () => {
    const { onChange } = renderWith({ mode: 'blend', sessions: ['Race'] });
    fireEvent.click(screen.getByRole('radio', { name: /official race laps only/i }));
    expect(onChange).toHaveBeenCalledWith({ mode: 'official', sessions: [] });
  });

  it('clicking a session pill adds it to sessions', () => {
    const { onChange } = renderWith({ mode: 'blend', sessions: [] });
    fireEvent.click(screen.getByRole('button', { name: 'Race' }));
    expect(onChange).toHaveBeenCalledWith({ mode: 'blend', sessions: ['Race'] });
  });

  it('clicking an active session pill removes it', () => {
    const { onChange } = renderWith({ mode: 'blend', sessions: ['Race'] });
    fireEvent.click(screen.getByRole('button', { name: 'Race' }));
    expect(onChange).toHaveBeenCalledWith({ mode: 'blend', sessions: [] });
  });

  it('session pill is aria-pressed when active', () => {
    renderWith({ mode: 'blend', sessions: ['Race'] });
    expect(screen.getByRole('button', { name: 'Race' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'Practice' })).toHaveAttribute(
      'aria-pressed',
      'false'
    );
  });

  it('applies extra className to root element', () => {
    const { container } = renderWith({ mode: 'official', sessions: [] });
    // className prop defaults to '' — root has class "pcs "
    expect(container.firstChild).toHaveClass('pcs');
  });
});
