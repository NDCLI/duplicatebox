import React from 'react';
import type { ImageResult } from '../App';

interface Props {
  result: ImageResult;
  onClick: () => void;
}

export const ResultCard: React.FC<Props> = ({ result, onClick }) => {
  // Lấy danh sách label và box ID liên quan đến lỗi
  const errorLabels = [...new Set(result.overlapGroups.flatMap(g => g.map(b => b.label)))];
  const errorBoxIds = [...new Set(result.overlapGroups.flatMap(g => g.map(b => b.id)))];

  return (
    <button
      type="button"
      onClick={onClick}
      className="group w-full text-left bg-[#1a1a1a] border border-white/10 rounded-2xl hover:border-red-400/50 hover:bg-[#222] transition-all cursor-pointer overflow-hidden"
    >
      <div className="p-4 flex items-start justify-between gap-3">
        <div className="flex items-start gap-3 min-w-0">
          <div className="w-9 h-9 rounded-xl bg-red-500/10 border border-red-500/20 flex items-center justify-center shrink-0 text-red-300">
            <span className="material-symbols-outlined text-[20px]">error</span>
          </div>
          <div className="flex flex-col min-w-0 gap-1">
            <span className="text-[12px] font-semibold text-white/90 truncate">Frame {result.id}</span>
            <span className="text-[11px] text-white/45 truncate">{result.name}</span>
            <span className="text-[10px] text-white/35 uppercase tracking-tighter">
              {result.boxes.length} box • {result.overlapGroups.length} cặp lỗi
            </span>
          </div>
        </div>
        <div className="bg-red-500/20 text-red-300 text-[10px] font-bold px-2 py-1 rounded-lg border border-red-500/30 shrink-0">
          {result.overlaps} LỖI
        </div>
      </div>

      {/* Label lỗi */}
      <div className="px-4 pb-2 flex flex-wrap gap-1">
        {errorLabels.map(label => (
          <span key={label} className="text-[9px] bg-amber-500/15 text-amber-300 border border-amber-500/20 px-1.5 py-0.5 rounded-md font-medium">
            {label}
          </span>
        ))}
      </div>

      {/* Box IDs */}
      <div className="px-4 pb-3">
        <span className="text-[9px] text-white/30">
          Box ID: {errorBoxIds.join(', ')}
        </span>
      </div>

      <div className="h-[1px] bg-white/5" />
      <div className="px-4 py-2 flex items-center justify-between text-[10px] text-white/35">
        <span>Click để tải ảnh gốc và soi lỗi</span>
        <span className="material-symbols-outlined text-[16px] opacity-50 group-hover:translate-x-0.5 transition-transform">chevron_right</span>
      </div>
    </button>
  );
};
