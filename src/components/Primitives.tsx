import React, { useState, useRef, useEffect } from 'react';

export const SectionLabel: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <div className="flex items-center px-2">
    <span className="text-[11px] font-medium text-[rgba(218,220,224,0.9)] tracking-[0.1px] normal-case">
      {children}
    </span>
  </div>
);

export const PillButton: React.FC<{
  icon?: React.ReactNode; children: React.ReactNode;
  variant?: 'filled' | 'outline' | 'solid'; onClick?: () => void;
}> = ({ icon, children, variant = 'filled', onClick }) => {
  const base = 'flex items-center gap-[2px] justify-center w-full h-[34px] rounded-xl font-medium tracking-[0.1px] transition-all cursor-pointer border-none outline-none';
  const variants: Record<string, string> = {
    filled: 'bg-[#969696] hover:bg-[#a6a6a6] active:bg-[#868686] text-black text-[11px] pl-[8px] pr-[24px] py-1 select-none',
    outline: 'border border-[#595959] hover:bg-white/5 active:bg-white/10 backdrop-blur-[40px] text-[12px] pl-[8px] pr-[16px] py-2 text-white select-none',
    solid: 'bg-white hover:bg-gray-200 active:bg-gray-300 text-black text-[12px] pl-[8px] pr-[16px] py-2 select-none',
  };
  return (
    <button className={`${base} ${variants[variant]}`} onClick={onClick}>
      {icon && <span className="flex items-center justify-center w-6 h-6">{icon}</span>}
      <span>{children}</span>
    </button>
  );
};

export const FieldDisplay: React.FC<{ label: string; value: string; className?: string }> = ({ label, value, className = '' }) => (
  <div className={`border border-[#595959] rounded-xl flex flex-col gap-0.5 justify-center pb-2 pl-2.5 pr-1 pt-[5px] select-none ${className}`}>
    <p className="text-[11px] font-medium text-[rgba(255,255,255,0.35)] tracking-[0.1px]">{label}</p>
    <div className="flex items-center">
      <span className="text-[11px] font-medium text-white tracking-[0.1px]">{value}</span>
    </div>
  </div>
);

export const DragNumberField: React.FC<{
  label: string; value: number; min?: number; max?: number;
  step?: number; suffix?: string; onChange: (val: number) => void; className?: string;
}> = ({ label, value, min = 0, max = 999, step = 1, suffix = 'px', onChange, className = '' }) => {
  const [isEditing, setIsEditing] = useState(false);
  const [editValue, setEditValue] = useState(String(value));
  const inputRef = useRef<HTMLInputElement>(null);
  const dragRef = useRef<{ startY: number; startVal: number; moved: boolean } | null>(null);

  const precision = step.toString().split('.')[1]?.length || 0;

  const commitEdit = (raw: string) => {
    const parsed = parseFloat(raw);
    if (!isNaN(parsed)) {
      const rounded = Math.round(parsed / step) * step;
      onChange(Math.min(max, Math.max(min, Number(rounded.toFixed(precision)))));
    }
    setIsEditing(false);
  };

  const handleMouseDown = (e: React.MouseEvent) => {
    if (isEditing) return;
    e.preventDefault();
    dragRef.current = { startY: e.clientY, startVal: value, moved: false };
    const handleMouseMove = (ev: MouseEvent) => {
      if (!dragRef.current) return;
      if (Math.abs(ev.clientY - dragRef.current.startY) > 3) dragRef.current.moved = true;
      if (dragRef.current.moved) {
        const delta = dragRef.current.startY - ev.clientY;
        const newVal = dragRef.current.startVal + delta * step;
        const rounded = Math.round(newVal / step) * step;
        onChange(Math.min(max, Math.max(min, Number(rounded.toFixed(precision)))));
        document.body.style.cursor = 'ns-resize';
      }
    };
    const handleMouseUp = () => {
      const wasDrag = dragRef.current?.moved;
      dragRef.current = null;
      document.removeEventListener('mousemove', handleMouseMove);
      document.removeEventListener('mouseup', handleMouseUp);
      document.body.style.cursor = '';
      if (!wasDrag) {
        setEditValue(String(value));
        setIsEditing(true);
        setTimeout(() => inputRef.current?.select(), 0);
      }
    };
    document.addEventListener('mousemove', handleMouseMove);
    document.addEventListener('mouseup', handleMouseUp);
  };

  return (
    <div className={`border border-[#595959] hover:border-[#7a7a7a] rounded-xl flex flex-col gap-0.5 justify-center pb-2 pl-2.5 pr-1 pt-[5px] select-none transition-colors ${isEditing ? '' : 'cursor-ns-resize'} ${className}`}
      onMouseDown={handleMouseDown}>
      <p className="text-[11px] font-medium text-[rgba(255,255,255,0.35)] tracking-[0.1px]">{label}</p>
      <div className="flex items-center justify-between">
        {isEditing ? (
          <input ref={inputRef} type="text" value={editValue}
            onChange={(e) => setEditValue(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') commitEdit(editValue); if (e.key === 'Escape') setIsEditing(false); }}
            onBlur={() => commitEdit(editValue)}
            className="bg-transparent text-[11px] font-medium text-white tracking-[0.1px] outline-none w-full border-none p-0 m-0" autoFocus />
        ) : (
          <>
            <span className="text-[11px] font-medium text-white tracking-[0.1px] cursor-text">{Number(value.toFixed(precision))}{suffix}</span>
            <div className="flex flex-col items-center mr-1.5 -gap-px text-white/40">
              <svg width="10" height="6" viewBox="0 0 10 6" fill="none"><path d="M5 0L9 5H1L5 0Z" fill="currentColor"/></svg>
              <svg width="10" height="6" viewBox="0 0 10 6" fill="none"><path d="M5 6L1 1H9L5 6Z" fill="currentColor"/></svg>
            </div>
          </>
        )}
      </div>
    </div>
  );
};

export const RangeSlider: React.FC<{
  label: string; value: number; min: number; max: number;
  step?: number; formatValue?: (val: number) => string;
  onChange: (val: number) => void;
}> = ({ label, value, min, max, step = 1, formatValue = (v) => String(v), onChange }) => (
  <div className="flex flex-col gap-2 pt-2 pb-[5px] w-full">
    <div className="flex items-center justify-between px-2 select-none">
      <span className="text-[11px] font-medium text-[rgba(218,220,224,0.9)] tracking-[0.1px]">{label}</span>
      <span className="text-[11px] font-medium text-[#c7c9cd] tracking-[0.1px]">{formatValue(value)}</span>
    </div>
    <div className="px-2 w-full flex items-center h-2">
      <input type="range" min={min} max={max} step={step} value={value}
        onChange={(e) => onChange(Number(e.target.value))} />
    </div>
  </div>
);

export const TextInput: React.FC<{
  label: string; value: string; onChange: (val: string) => void; placeholder?: string;
}> = ({ label, value, onChange, placeholder }) => (
  <div className="flex flex-col gap-1 w-full">
    <p className="text-[11px] font-medium text-[rgba(255,255,255,0.35)] tracking-[0.1px] pl-2">{label}</p>
    <input 
      type="text"
      value={value} 
      onChange={(e) => onChange(e.target.value)} 
      placeholder={placeholder}
      className="border border-[#595959] hover:border-[#7a7a7a] focus:border-[#969696] rounded-xl w-full h-[40px] px-3 bg-transparent text-[11px] font-medium text-white placeholder-[rgba(218,220,224,0.35)] tracking-[0.1px] focus:outline-none transition-colors" 
    />
  </div>
);
