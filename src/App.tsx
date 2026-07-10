import React, { useState, useEffect, useMemo, useCallback } from 'react';
import type { Entry } from '@zip.js/zip.js';
import { ZipReader, BlobReader, TextWriter } from '@zip.js/zip.js';
import { SectionLabel, PillButton, DragNumberField, RangeSlider, FieldDisplay } from './components/Primitives';
import { ResultCard } from './components/ResultCard';
import { ZoomInspector } from './components/ZoomInspector';

export interface Box {
  id: string;
  xtl: number;
  ytl: number;
  xbr: number;
  ybr: number;
  label: string;
}

export interface RawFrame {
  id: string;
  name: string;
  width: number;
  height: number;
  boxes: Box[];
}

export interface ImageResult extends RawFrame {
  overlaps: number;
  overlapGroups: Box[][];
}

interface AuditStats {
  totalImages: number;
  totalBoxes: number;
  totalOverlaps: number;
  duplicateCount: number;
}

export default function App() {
  const [iouThreshold, setIouThreshold] = useState(1.0);
  const [pixelTolerance, setPixelTolerance] = useState(0);
  
  const [rawFrames, setRawFrames] = useState<RawFrame[]>([]);
  const [startFrame, setStartFrame] = useState<number>(0);
  const [endFrame, setEndFrame] = useState<number>(0);

  const [isProcessing, setIsProcessing] = useState(false);
  const [progress, setProgress] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [zipEntries, setZipEntries] = useState<Entry[] | null>(null);
  const [selectedResult, setSelectedResult] = useState<ImageResult | null>(null);

  useEffect(() => {
    const id = 'cvat-audit-styles';
    if (document.getElementById(id)) return;
    const style = document.createElement('style');
    style.id = id;
    style.textContent = `
      input[type=range] { -webkit-appearance: none; appearance: none; background: transparent; width: 100%; cursor: pointer; padding: 8px 0; }
      input[type=range]::-webkit-slider-runnable-track { width: 100%; height: 3px; background: #595959; border-radius: 9999px; }
      input[type=range]::-webkit-slider-thumb { -webkit-appearance: none; appearance: none; width: 14px; height: 14px; border-radius: 50%; background: white; box-shadow: 0px 1px 3px rgba(0,0,0,0.5); margin-top: -5.5px; cursor: grab; }
      .dark-scrollbar { scrollbar-width: thin; scrollbar-color: #595959 transparent; }
      .dark-scrollbar::-webkit-scrollbar { width: 6px; }
      .dark-scrollbar::-webkit-scrollbar-thumb { background: #595959; border-radius: 9999px; }
      @keyframes dropdown-enter { from { opacity: 0; transform: scale(0.95) translateY(-5px); } to { opacity: 1; transform: scale(1) translateY(0); } }
      .animate-dropdown { animation: dropdown-enter 0.15s ease-out forwards; }
      html, body, #root { margin: 0; padding: 0; width: 100%; height: 100%; background: #0e0e0e; font-family: 'Google Sans Text', 'Google Sans', sans-serif; letter-spacing: 0.1px; }
    `;
    document.head.appendChild(style);
  }, []);

  const calculateIoU = useCallback((b1: Box, b2: Box): number => {
    const xLeft = Math.max(b1.xtl, b2.xtl);
    const yTop = Math.max(b1.ytl, b2.ytl);
    const xRight = Math.min(b1.xbr, b2.xbr);
    const yBottom = Math.min(b1.ybr, b2.ybr);
    if (xRight <= xLeft || yBottom <= yTop) return 0;
    const intersectionArea = (xRight - xLeft) * (yBottom - yTop);
    const b1Area = (b1.xbr - b1.xtl) * (b1.ybr - b1.ytl);
    const b2Area = (b2.xbr - b2.xtl) * (b2.ybr - b2.ytl);
    return intersectionArea / (b1Area + b2Area - intersectionArea);
  }, []);

  const isEdgeMatched = useCallback((b1: Box, b2: Box, tol: number): boolean => {
    return Math.abs(b1.xtl - b2.xtl) <= tol && Math.abs(b1.ytl - b2.ytl) <= tol
      && Math.abs(b1.xbr - b2.xbr) <= tol && Math.abs(b1.ybr - b2.ybr) <= tol;
  }, []);

  const checkOverlaps = useCallback((boxes: Box[]) => {
    let overlapCount = 0;
    let duplicateCount = 0;
    const groups: Box[][] = [];
    const sorted = [...boxes].sort((a, b) => a.xtl - b.xtl);
    for (let i = 0; i < sorted.length; i++) {
      for (let j = i + 1; j < sorted.length; j++) {
        const b1 = sorted[i];
        const b2 = sorted[j];
        if (b2.xtl > b1.xbr + pixelTolerance) break;
        const iou = calculateIoU(b1, b2);
        const edgeMatched = isEdgeMatched(b1, b2, pixelTolerance);
        if (iou >= iouThreshold || edgeMatched) {
          overlapCount++;
          groups.push([b1, b2]);
          if (edgeMatched && b1.label === b2.label) duplicateCount++;
        }
      }
    }
    return { overlapCount, duplicateCount, groups };
  }, [iouThreshold, pixelTolerance, calculateIoU, isEdgeMatched]);

  const processXML = async (xmlText: string) => {
    const parser = new DOMParser();
    const xmlDoc = parser.parseFromString(xmlText, 'text/xml');
    if (xmlDoc.querySelector('parsererror')) throw new Error('Định dạng XML không hợp lệ hoặc tệp quá lớn.');
    
    const imageNodes = Array.from(xmlDoc.querySelectorAll('image'));
    const parsedFrames: RawFrame[] = [];
    
    const chunkSize = 200;
    for (let i = 0; i < imageNodes.length; i += chunkSize) {
      setProgress(Math.round((i / imageNodes.length) * 100));
      await new Promise(r => setTimeout(r, 0));
      imageNodes.slice(i, i + chunkSize).forEach(node => {
        const boxes: Box[] = Array.from(node.querySelectorAll('box')).map((b, idx) => ({
          id: b.getAttribute('id') || `b-${idx}`,
          xtl: parseFloat(b.getAttribute('xtl') || '0'),
          ytl: parseFloat(b.getAttribute('ytl') || '0'),
          xbr: parseFloat(b.getAttribute('xbr') || '0'),
          ybr: parseFloat(b.getAttribute('ybr') || '0'),
          label: b.getAttribute('label') || 'unlabeled'
        }));
        parsedFrames.push({
          id: node.getAttribute('id') || 'unknown',
          name: node.getAttribute('name') || 'unknown',
          width: parseInt(node.getAttribute('width') || '0'),
          height: parseInt(node.getAttribute('height') || '0'),
          boxes
        });
      });
    }
    
    setRawFrames(parsedFrames);
    
    // Set default start and end frames
    const frameIds = parsedFrames.map(f => parseInt(f.id, 10)).filter(n => !isNaN(n));
    if (frameIds.length > 0) {
      setStartFrame(Math.min(...frameIds));
      setEndFrame(Math.max(...frameIds));
    } else {
      setStartFrame(0);
      setEndFrame(0);
    }
    
    setProgress(100);
  };

  const handleFileUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    const fileName = file.name.toLowerCase();
    const fileBlob = file.slice(0, file.size, file.type);

    setIsProcessing(true);
    setError(null);
    setProgress(0);
    setRawFrames([]);
    setZipEntries(null);

    if (e.target) e.target.value = '';

    try {
      if (fileName.endsWith('.zip')) {
        const zipReader = new ZipReader(new BlobReader(fileBlob));
        const entries = await zipReader.getEntries();
        setZipEntries(entries);
        const xmlEntry = entries.find(entry =>
          entry.filename.toLowerCase().endsWith('.xml') && !entry.filename.startsWith('__MACOSX/')
        );
        if (!xmlEntry) throw new Error('Không tìm thấy tệp XML bên trong ZIP.');
        if (typeof (xmlEntry as any).getData !== 'function') {
          throw new Error('Tệp ZIP không chứa dữ liệu XML có thể đọc được.');
        }
        const xmlText = await (xmlEntry as any).getData(new TextWriter());
        await processXML(xmlText);
      } else if (fileName.endsWith('.xml')) {
        const text = await fileBlob.text();
        await processXML(text);
      } else {
        throw new Error('Định dạng không được hỗ trợ. Vui lòng sử dụng .xml hoặc .zip.');
      }
    } catch (err: any) {
      console.error("Lỗi xử lý:", err);
      setError(err.message || 'Lỗi truy cập dữ liệu. Thử tải lại tệp.');
    } finally {
      setIsProcessing(false);
    }
  };

  // Tính toán real-time khi iou, px hoặc frame bounds thay đổi
  const { results, stats } = useMemo(() => {
    if (rawFrames.length === 0) return { results: [], stats: null };

    let totalBoxes = 0, totalOverlaps = 0, totalDuplicates = 0;
    const auditResults: ImageResult[] = [];

    const filteredFrames = rawFrames.filter(f => {
      const id = parseInt(f.id, 10);
      if (isNaN(id)) return true;
      return id >= startFrame && id <= endFrame;
    });

    filteredFrames.forEach(f => {
      totalBoxes += f.boxes.length;
      const { overlapCount, duplicateCount, groups } = checkOverlaps(f.boxes);
      totalOverlaps += overlapCount;
      totalDuplicates += duplicateCount;
      if (overlapCount > 0) {
        auditResults.push({ ...f, overlaps: overlapCount, overlapGroups: groups });
      }
    });

    return {
      results: auditResults,
      stats: {
        totalImages: filteredFrames.length,
        totalBoxes,
        totalOverlaps,
        duplicateCount: totalDuplicates
      } as AuditStats
    };
  }, [rawFrames, startFrame, endFrame, checkOverlaps]);

  const exportCSV = () => {
    if (!stats || results.length === 0) return;
    let csv = '\uFEFF';
    csv += 'Frame ID,Frame Name,Overlap Count,Boxes Detail\n';
    results.forEach(r => {
      csv += `${r.id},${r.name},${r.overlaps},"${r.boxes.length} boxes"\n`;
    });
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'cvat_audit_report.csv';
    a.click();
    URL.revokeObjectURL(url);
  };

  return (
    <div className="flex h-screen w-screen bg-[#0e0e0e] text-white">
      {/* Left Panel */}
      <div className="w-[300px] h-full border-r border-[rgba(218,220,224,0.15)] flex flex-col p-[10px] shrink-0">
        <div className="flex flex-col gap-[24px] flex-1 overflow-clip">
          <div className="flex flex-col gap-2">
            <SectionLabel>Tệp Dữ Liệu</SectionLabel>
            <input type="file" id="cvat-up" accept=".xml,.zip" className="hidden" onChange={handleFileUpload} disabled={isProcessing} />
            <label htmlFor="cvat-up"
              className={`flex items-center gap-2 justify-center w-full h-[34px] rounded-xl font-medium text-[12px] cursor-pointer transition-all ${isProcessing ? 'bg-white/10 text-white/40 pointer-events-none' : 'bg-white text-black hover:bg-gray-200'}`}>
              <span className={`material-symbols-outlined text-[18px] ${isProcessing ? 'animate-spin' : ''}`}>
                {isProcessing ? 'sync' : 'upload_file'}
              </span>
              {isProcessing ? 'Đang nạp dữ liệu...' : 'Tải XML hoặc ZIP'}
            </label>
          </div>

          <div className="flex flex-col gap-2">
            <SectionLabel>Tiêu Chí Quét</SectionLabel>
            <div className="flex flex-col gap-2">
              <RangeSlider label="Ngưỡng IoU" value={iouThreshold} min={0.5} max={1} step={0.01} formatValue={v => `${(v * 100).toFixed(0)}%`} onChange={setIouThreshold} />
              <DragNumberField label="Sai số cạnh" value={pixelTolerance} min={0} max={20} step={0.1} suffix="px" onChange={setPixelTolerance} />
            </div>
          </div>

          {stats && (
            <div className="flex flex-col gap-2 animate-dropdown">
              <SectionLabel>Thống Kê</SectionLabel>
              <div className="flex flex-col gap-1.5">
                <div className="flex gap-1">
                  <FieldDisplay label="Tổng Frame" value={stats.totalImages.toLocaleString()} className="flex-1" />
                  <FieldDisplay label="Tổng Box" value={stats.totalBoxes.toLocaleString()} className="flex-1" />
                </div>
                <div className="flex gap-1">
                  <FieldDisplay label="Tổng Lỗi" value={stats.totalOverlaps.toLocaleString()} className="flex-1" />
                  <FieldDisplay label="Trùng Exact" value={stats.duplicateCount.toLocaleString()} className="flex-1" />
                </div>
                <div className="flex gap-1 mt-1">
                  <DragNumberField label="Từ Frame" value={startFrame} min={0} suffix="" onChange={setStartFrame} className="flex-1" />
                  <DragNumberField label="Đến Frame" value={endFrame} min={0} suffix="" onChange={setEndFrame} className="flex-1" />
                </div>
              </div>
            </div>
          )}

          {error && (
            <div className="p-3 rounded-xl bg-red-500/10 border border-red-500/20 text-red-400 text-[11px] mt-2 animate-pulse">
              <div className="flex gap-2 items-start">
                <span className="material-symbols-outlined text-[16px]">warning</span>
                <span className="leading-tight">{error}</span>
              </div>
            </div>
          )}
        </div>

        <div className="flex flex-col gap-[5px] mt-auto">
          <PillButton variant="outline" icon={<span className="material-symbols-outlined text-[18px]">download</span>} onClick={exportCSV}>
            Xuất Báo Cáo CSV
          </PillButton>
          <PillButton variant="solid" icon={<span className="material-symbols-outlined text-[18px]">refresh</span>} onClick={() => window.location.reload()}>
            Làm Mới Trang
          </PillButton>
        </div>
      </div>

      {/* Main Content */}
      <div className="flex-1 h-full overflow-hidden flex flex-col p-6">
        <header className="mb-6">
          <h1 className="text-xl font-medium text-white/90">CVAT Box Auditor Pro</h1>
          <p className="text-sm text-white/40">Phát hiện và thanh lọc các bounding box bị trùng lấn.</p>
        </header>

        <div className="flex-1 overflow-y-auto dark-scrollbar pr-2">
          {isProcessing ? (
            <div className="h-full flex flex-col items-center justify-center gap-4">
              <div className="w-16 h-16 border-2 border-white/10 border-t-white rounded-full animate-spin" />
              <div className="text-center">
                <p className="text-sm font-medium">Đang nạp tệp vào RAM... {progress}%</p>
                <p className="text-xs text-white/30 italic mt-1">Vui lòng không đóng trình duyệt trong khi xử lý.</p>
              </div>
            </div>
          ) : results.length > 0 ? (
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
              {results.map(r => (
                <ResultCard key={r.id} result={r} onClick={() => setSelectedResult(r)} />
              ))}
            </div>
          ) : (
            <div className="h-full flex flex-col items-center justify-center text-white/10 gap-4">
              <span className="material-symbols-outlined text-[80px]">folder_zip</span>
              <p className="text-sm max-w-xs text-center leading-relaxed">
                Tải lên tệp .zip (bao gồm ảnh) để có trải nghiệm trực quan tốt nhất, hoặc tệp .xml để quét dữ liệu nhanh.
              </p>
            </div>
          )}
        </div>
      </div>

      {/* Detail Inspector Modal */}
      {selectedResult && (
        <ZoomInspector result={selectedResult} zipEntries={zipEntries} onClose={() => setSelectedResult(null)} />
      )}
    </div>
  );
}
