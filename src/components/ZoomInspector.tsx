import React, { useState, useEffect, useRef } from 'react';
import type { Entry } from '@zip.js/zip.js';
import { BlobWriter } from '@zip.js/zip.js';
import type { ImageResult, Box } from '../App';
import { PillButton } from './Primitives';

interface Props {
  result: ImageResult;
  zipEntries: Entry[] | null;
  onClose: () => void;
}

export const ZoomInspector: React.FC<Props> = ({ result, zipEntries, onClose }) => {
  const [imageSrc, setImageSrc] = useState<string | null>(null);
  const [isZoomed, setIsZoomed] = useState(false);
  const [viewBox, setViewBox] = useState(`0 0 ${result.width} ${result.height}`);
  const [selectedBox, setSelectedBox] = useState<Box | null>(null);

  // Manual Zoom & Pan State
  const [transform, setTransform] = useState({ scale: 1, x: 0, y: 0 });
  const isDragging = useRef(false);
  const startPos = useRef({ x: 0, y: 0 });

  useEffect(() => {
    let active = true;
    const loadFull = async () => {
      if (!zipEntries) return;
      const fileName = result.name.split(/[/\\]/).pop() || '';
      const fileEntry = zipEntries.find(f => f.filename.endsWith(fileName));
      if (fileEntry && fileEntry.getData) {
        const blob = await fileEntry.getData(new BlobWriter());
        const url = URL.createObjectURL(blob);
        if (active) setImageSrc(url);
      }
    };
    loadFull();
    return () => { active = false; if (imageSrc) URL.revokeObjectURL(imageSrc); };
  }, [result.name, zipEntries]);

  const handleZoomToggle = () => {
    setTransform({ scale: 1, x: 0, y: 0 }); // Reset manual zoom
    if (isZoomed) {
      setViewBox(`0 0 ${result.width} ${result.height}`);
      setIsZoomed(false);
    } else {
      // Zoom vào nhóm lỗi đầu tiên
      const group = result.overlapGroups[0];
      if (group && group.length > 0) {
        const minX = Math.min(...group.map(b => b.xtl));
        const minY = Math.min(...group.map(b => b.ytl));
        const maxX = Math.max(...group.map(b => b.xbr));
        const maxY = Math.max(...group.map(b => b.ybr));
        const w = maxX - minX;
        const h = maxY - minY;
        const pad = 100;
        setViewBox(`${Math.max(0, minX - pad)} ${Math.max(0, minY - pad)} ${w + pad*2} ${h + pad*2}`);
        setIsZoomed(true);
      }
    }
  };

  const containerRef = useRef<HTMLDivElement>(null);

  const handleWheel = (e: React.WheelEvent) => {
    if (!containerRef.current) return;
    const rect = containerRef.current.getBoundingClientRect();
    const mouseX = e.clientX - rect.left;
    const mouseY = e.clientY - rect.top;

    const scaleAdjust = e.deltaY > 0 ? 0.9 : 1.1;
    setTransform(prev => {
      const newScale = Math.max(0.1, Math.min(20, prev.scale * scaleAdjust));
      const newTx = mouseX - ((mouseX - prev.x) / prev.scale) * newScale;
      const newTy = mouseY - ((mouseY - prev.y) / prev.scale) * newScale;
      return { scale: newScale, x: newTx, y: newTy };
    });
  };

  const handlePointerDown = (e: React.PointerEvent) => {
    // Chỉ drag khi ấn chuột giữa hoặc chuột trái ngoài SVG path
    isDragging.current = true;
    startPos.current = { x: e.clientX - transform.x, y: e.clientY - transform.y };
  };

  const handlePointerMove = (e: React.PointerEvent) => {
    if (!isDragging.current) return;
    setTransform(prev => ({
      ...prev,
      x: e.clientX - startPos.current.x,
      y: e.clientY - startPos.current.y
    }));
  };

  const handlePointerUp = () => {
    isDragging.current = false;
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/90 backdrop-blur-md animate-dropdown">
      <div className="bg-[#1a1a1a] border border-white/10 rounded-2xl w-full max-w-6xl h-[90vh] flex flex-col overflow-hidden shadow-2xl">
        <header className="p-4 border-b border-white/10 flex justify-between items-center bg-[#1a1a1a] z-10">
          <div className="flex flex-col">
            <h2 className="text-lg font-medium">{result.name}</h2>
            <p className="text-xs text-white/40">Phát hiện {result.overlaps} vùng trùng lấn tọa độ</p>
          </div>
          <div className="flex gap-2">
            <PillButton variant="outline" icon={<span className="material-symbols-outlined text-[18px]">zoom_in</span>} onClick={handleZoomToggle}>
              {isZoomed ? 'Toàn cảnh' : 'Soi vùng lỗi'}
            </PillButton>
            <button onClick={onClose} className="w-8 h-8 flex items-center justify-center rounded-full hover:bg-white/10 transition-colors">
              <span className="material-symbols-outlined text-white/60">close</span>
            </button>
          </div>
        </header>

        <div 
          ref={containerRef}
          className="flex-1 relative bg-black flex items-center justify-center overflow-hidden cursor-move"
          onWheel={handleWheel}
          onPointerDown={handlePointerDown}
          onPointerMove={handlePointerMove}
          onPointerUp={handlePointerUp}
          onPointerLeave={handlePointerUp}
        >
          <div 
            style={{ 
              transform: `translate(${transform.x}px, ${transform.y}px) scale(${transform.scale})`, 
              transformOrigin: '0 0' 
            }} 
            className="w-full h-full flex items-center justify-center transition-transform duration-75"
          >
            <svg 
              viewBox={viewBox} 
              className="w-full h-full object-contain cursor-crosshair transition-all duration-500"
            >
              {imageSrc && <image href={imageSrc} width={result.width} height={result.height} />}
              {/* Vẽ tất cả các box */}
              {result.boxes.map((box, i) => {
                const isOverlap = result.overlapGroups.some(g => g.some(gb => gb.id === box.id));
                return (
                  <rect 
                    key={i}
                    x={box.xtl} y={box.ytl} 
                    width={box.xbr - box.xtl} height={box.ybr - box.ytl}
                    fill={isOverlap ? 'rgba(239, 68, 68, 0.1)' : 'transparent'}
                    stroke={isOverlap ? '#ef4444' : 'rgba(255,255,255,0.3)'}
                    strokeWidth={isOverlap ? 3 : 1}
                    className="transition-all hover:stroke-yellow-400"
                    onClick={(e) => { e.stopPropagation(); setSelectedBox(box); }}
                  />
                );
              })}
            </svg>
          </div>

          {selectedBox && (
            <div className="absolute top-4 right-4 bg-[#1a1a1a] border border-white/20 p-3 rounded-xl shadow-xl w-60 animate-dropdown z-10">
              <h4 className="text-xs font-bold text-red-400 mb-2 uppercase tracking-widest">Chi tiết Bounding Box</h4>
              <div className="space-y-1.5">
                <div className="flex justify-between text-[11px]"><span className="text-white/40">Nhãn:</span><span className="font-bold">{selectedBox.label}</span></div>
                <div className="flex justify-between text-[11px]"><span className="text-white/40">XTL:</span><span>{selectedBox.xtl.toFixed(2)}</span></div>
                <div className="flex justify-between text-[11px]"><span className="text-white/40">YTL:</span><span>{selectedBox.ytl.toFixed(2)}</span></div>
                <div className="flex justify-between text-[11px]"><span className="text-white/40">XBR:</span><span>{selectedBox.xbr.toFixed(2)}</span></div>
                <div className="flex justify-between text-[11px]"><span className="text-white/40">YBR:</span><span>{selectedBox.ybr.toFixed(2)}</span></div>
              </div>
              <button onClick={() => setSelectedBox(null)} className="mt-3 w-full py-1 text-[10px] bg-white/5 hover:bg-white/10 rounded uppercase font-bold transition-colors">Đóng</button>
            </div>
          )}
        </div>

        <footer className="p-4 border-t border-white/10 bg-[#0e0e0e] flex gap-4 overflow-x-auto dark-scrollbar z-10">
          {result.overlapGroups.map((g, idx) => (
            <div 
              key={idx} 
              className="flex-shrink-0 bg-white/5 p-2 rounded-lg border border-red-500/30 text-[10px] flex flex-col gap-1 min-w-[120px] cursor-pointer hover:bg-white/10"
              onClick={() => {
                setTransform({ scale: 1, x: 0, y: 0 }); // Reset manual zoom
                const minX = Math.min(...g.map(b => b.xtl));
                const minY = Math.min(...g.map(b => b.ytl));
                const maxX = Math.max(...g.map(b => b.xbr));
                const maxY = Math.max(...g.map(b => b.ybr));
                const pad = 80;
                setViewBox(`${Math.max(0, minX - pad)} ${Math.max(0, minY - pad)} ${maxX - minX + pad*2} ${maxY - minY + pad*2}`);
                setIsZoomed(true);
              }}
            >
              <span className="font-bold text-red-400">CẶP LỖI #{idx + 1}</span>
              <span className="opacity-60">{g[0].label} ↔ {g[1].label}</span>
            </div>
          ))}
        </footer>
      </div>
    </div>
  );
};
