import React, { useState, useEffect, useRef, useMemo } from 'react';
import {
  FileCheck,
  Layers,
  AlertTriangle,
  Trash2,
  
  Settings,
  Search,
  Download,
  RefreshCw,
  FileUp,
  X,
  ChevronRight,
  ChevronLeft,
  Sliders,
  Maximize2,
  Minimize2,
  Grid,
  Eye,
  HelpCircle,
  Info,
  Sparkles,
  CheckCircle,
  FileCode,
  FileArchive,
  Image as ImageIcon,
  Loader2
} from 'lucide-react';
import { motion, AnimatePresence } from 'motion/react';
import { ZipReader, BlobReader, BlobWriter, TextWriter, TextReader, ZipWriter, Entry } from '@zip.js/zip.js';
import { parseCVATXML, detectDuplicates, removeDuplicatesFromXML, generateCSVReport } from './utils/parser';

import { CVATDataset, DuplicateGroup, DetectionSettings, CVATBox } from './types';



function CustomZoomPanPinch({ 
  children, 
  contentWidth, 
  contentHeight, 
  onZoomToElementRef 
}) {
  const stageRef = useRef(null);
  const dragRef = useRef(null);
  const txRef = useRef({ scale: 1, panX: 0, panY: 0 });
  const [tx, setTx] = useState({ scale: 1, panX: 0, panY: 0 });
  const [isAnimating, setIsAnimating] = useState(false);
  const animTimeoutRef = useRef(null);

  const MIN_ZOOM = 0.05;
  const MAX_ZOOM = 50;

  function constrainViewerPan(tx, stage) {
    if (!stage || !contentWidth || !contentHeight) return tx;
    const { scale, panX, panY } = tx;
    const bounds = stage.getBoundingClientRect();
    
    const imgWidth = contentWidth * scale;
    const imgHeight = contentHeight * scale;

    const maxPanX = imgWidth + bounds.width * 5;
    const maxPanY = imgHeight + bounds.height * 5;

    const constrainedPanX = Math.min(Math.max(panX, -maxPanX), maxPanX);
    const constrainedPanY = Math.min(Math.max(panY, -maxPanY), maxPanY);

    return { scale, panX: constrainedPanX, panY: constrainedPanY };
  }

  function updateTx(nextTx, animated = false) {
    if (!nextTx || isNaN(nextTx.scale) || isNaN(nextTx.panX) || isNaN(nextTx.panY)) {
      console.error('Invalid zoom Tx', nextTx);
      return;
    }
    const stage = stageRef.current;
    if (stage) {
      nextTx = constrainViewerPan(nextTx, stage);
    }
    
    if (animated) {
      setIsAnimating(true);
      if (animTimeoutRef.current) clearTimeout(animTimeoutRef.current);
      animTimeoutRef.current = setTimeout(() => setIsAnimating(false), 800);
    } else {
      setIsAnimating(false);
      if (animTimeoutRef.current) clearTimeout(animTimeoutRef.current);
    }
    
    txRef.current = nextTx;
    setTx(nextTx);
  }

  useEffect(() => {
    if (onZoomToElementRef) {
      onZoomToElementRef.current = {
        zoomToElement: (elementId, padding = 0, duration = 800, easing = 'easeOut') => {
          const el = typeof elementId === 'string' ? document.getElementById(elementId) : elementId;
          if (el && stageRef.current && contentWidth && contentHeight) {
            let x = 0, y = 0, width = 0, height = 0;
            
            // Try to read SVG attributes if available (much more reliable)
            if (el.hasAttribute('x') && el.hasAttribute('y') && el.hasAttribute('width') && el.hasAttribute('height')) {
              x = parseFloat(el.getAttribute('x')) || 0;
              y = parseFloat(el.getAttribute('y')) || 0;
              width = parseFloat(el.getAttribute('width')) || 0;
              height = parseFloat(el.getAttribute('height')) || 0;
            } else {
              // Fallback to bounding rect calculations
              const contentContainer = stageRef.current.firstElementChild;
              const contentRect = contentContainer.getBoundingClientRect();
              const elRect = el.getBoundingClientRect();
              
              const currentScale = txRef.current.scale;
              
              x = (elRect.left - contentRect.left) / currentScale;
              y = (elRect.top - contentRect.top) / currentScale;
              width = elRect.width / currentScale;
              height = elRect.height / currentScale;
            }
            
            if (width > 0 && height > 0) {
              const stageBounds = stageRef.current.getBoundingClientRect();
              
              // Add some visual padding around the zoomed element
              const paddedWidth = width + 50; 
              const paddedHeight = height + 50;

              const scaleX = stageBounds.width / paddedWidth;
              const scaleY = stageBounds.height / paddedHeight;
              
              // Cap the maximum scale so it doesn't zoom in crazy much for tiny boxes
              const targetScale = Math.min(scaleX, scaleY, 5); 
              const newScale = Math.min(Math.max(targetScale, MIN_ZOOM), MAX_ZOOM);
              
              const newPanX = stageBounds.width / 2 - (x + width / 2) * newScale;
              const newPanY = stageBounds.height / 2 - (y + height / 2) * newScale;
              
              updateTx({ scale: newScale, panX: newPanX, panY: newPanY }, true); // animate!
            }
          }
        }
      };
    }
  }, [onZoomToElementRef, contentWidth, contentHeight]);

  useEffect(() => {
    const stage = stageRef.current;
    if (stage && contentWidth && contentHeight) {
      const bounds = stage.getBoundingClientRect();
      const scaleX = bounds.width / contentWidth;
      const scaleY = bounds.height / contentHeight;
      const fitScale = Math.min(scaleX, scaleY, 1);
      
      const panX = (bounds.width - contentWidth * fitScale) / 2;
      const panY = (bounds.height - contentHeight * fitScale) / 2;
      
      updateTx({ scale: fitScale, panX, panY }, false);
    }
  }, [contentWidth, contentHeight]);

  useEffect(() => {
    const stage = stageRef.current;
    if (!stage) return;

    function onWheel(event) {
      event.preventDefault();
      const { scale, panX, panY } = txRef.current;
      const bounds = stage.getBoundingClientRect();
      const cx = event.clientX - bounds.left;
      const cy = event.clientY - bounds.top;

      const ix = (cx - panX) / scale;
      const iy = (cy - panY) / scale;

      const zoomFactor = event.deltaY < 0 ? 1.1 : 0.9;
      let newScale = scale * zoomFactor;
      newScale = Math.min(Math.max(newScale, MIN_ZOOM), MAX_ZOOM);

      const newPanX = cx - ix * newScale;
      const newPanY = cy - iy * newScale;

      updateTx({ scale: newScale, panX: newPanX, panY: newPanY }, false);
    }

    stage.addEventListener('wheel', onWheel, { passive: false });
    return () => stage.removeEventListener('wheel', onWheel);
  }, []);

  function handleMouseDown(event) {
    event.preventDefault();
    dragRef.current = { lastX: event.clientX, lastY: event.clientY };
    document.body.style.cursor = 'grabbing';
  }

  function handleMouseMove(event) {
    if (!dragRef.current) return;
    const dx = event.clientX - dragRef.current.lastX;
    const dy = event.clientY - dragRef.current.lastY;
    dragRef.current = { lastX: event.clientX, lastY: event.clientY };
    
    const { scale, panX, panY } = txRef.current;
    updateTx({ scale, panX: panX + dx, panY: panY + dy }, false);
  }

  function handleMouseUp() {
    dragRef.current = null;
    document.body.style.cursor = '';
  }

  function handleDoubleClick(event) {
    const stage = stageRef.current;
    if (stage && contentWidth && contentHeight) {
      const bounds = stage.getBoundingClientRect();
      const scaleX = bounds.width / contentWidth;
      const scaleY = bounds.height / contentHeight;
      const fitScale = Math.min(scaleX, scaleY, 1);
      
      const panX = (bounds.width - contentWidth * fitScale) / 2;
      const panY = (bounds.height - contentHeight * fitScale) / 2;
      
      updateTx({ scale: fitScale, panX, panY }, true);
    }
  }

  return (
    <div 
      ref={stageRef}
      className="w-full h-full relative overflow-hidden"
      style={{ cursor: tx.scale > 1 ? 'grab' : 'default' }}
      onMouseDown={handleMouseDown}
      onMouseMove={handleMouseMove}
      onMouseUp={handleMouseUp}
      onMouseLeave={handleMouseUp}
      onDoubleClick={handleDoubleClick}
    >
      <div 
        style={{
          position: 'absolute',
          top: 0,
          left: 0,
          transform: `translate(${tx.panX}px, ${tx.panY}px) scale(${tx.scale})`,
          transformOrigin: '0 0',
          width: contentWidth ? `${contentWidth}px` : '100%',
          height: contentHeight ? `${contentHeight}px` : '100%',
          transition: isAnimating ? 'transform 0.6s cubic-bezier(0.22, 1, 0.36, 1)' : 'none',
        }}
      >
        {children({ state: { scale: tx.scale } })}
      </div>
    </div>
  );
}

const PALETTE = [
  '#ef4444', '#f97316', '#f59e0b', '#84cc16', '#22c55e', '#10b981', '#14b8a6', 
  '#06b6d4', '#0ea5e9', '#3b82f6', '#6366f1', '#8b5cf6', '#a855f7', '#d946ef', '#f43f5e'
];

const HARDCODED_LABEL_COLORS: Record<string, string> = {
  person: '#c06060',
  car: '#2080c0',
  motorbike: '#00a0a0',
  bicycle: '#004040',
  bus: '#204080',
  truck: '#906080',
  train: '#50a080',
  face: '#90e8ce',
  head: '#ba9109',
  _skip: '#766433',
  negative: '#e6213a',
  licenseplate: '#e277ef'
};

const getLabelColor = (label: string, dataset?: CVATDataset | null) => {
  if (dataset?.labelColors?.[label]) {
    return dataset.labelColors[label];
  }
  if (HARDCODED_LABEL_COLORS[label]) {
    return HARDCODED_LABEL_COLORS[label];
  }
  let hash = 0;
  for (let i = 0; i < label.length; i++) {
    hash = label.charCodeAt(i) + ((hash << 5) - hash);
  }
  return PALETTE[Math.abs(hash) % PALETTE.length];
};

export default function App() {
  // File uploading states
  const [file, setFile] = useState<File | null>(null);
  const [isDragging, setIsDragging] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  // ZIP specific states
  const [zipEntries, setZipEntries] = useState<Entry[] | null>(null);
  const [xmlFilesInZip, setXmlFilesInZip] = useState<string[]>([]);
  const [selectedXmlPath, setSelectedXmlPath] = useState<string>('');

  // Image states from ZIP
  const [currentImageSrc, setCurrentImageSrc] = useState<string | null>(null);
  const [imageLoading, setImageLoading] = useState(false);
    
  // Manual image uploads mapping for single frame view
  const [manualImages, setManualImages] = useState<Record<string, string>>({});

  // CVAT Dataset states
  const [xmlContent, setXmlContent] = useState<string>('');
  const [xmlFilename, setXmlFilename] = useState<string>('');
  const [dataset, setDataset] = useState<CVATDataset | null>(null);

  // Settings state
  const [settings, setSettings] = useState<DetectionSettings>({
    matchLabelOnly: true,
    tolerancePx: 0.0,
    overlapThreshold: 100.0,
    useIoU: true
  });

  // UI state
  const [searchTerm, setSearchTerm] = useState('');
  const [selectedLabels, setSelectedLabels] = useState<string[]>([]);
  const [selectedGroupId, setSelectedGroupId] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<'all' | 'duplicates'>('all');

  // Frame range filter
  const [frameRangeStart, setFrameRangeStart] = useState<string>('');
  const [frameRangeEnd, setFrameRangeEnd] = useState<string>('');

  useEffect(() => {
    if (dataset && dataset.frames.length > 0) {
      const ids = dataset.frames.map(f => parseInt(f.id, 10)).filter(n => !isNaN(n));
      if (ids.length > 0) {
        setFrameRangeStart(String(Math.min(...ids)));
        setFrameRangeEnd(String(Math.max(...ids)));
      }
    }
  }, [dataset]);

  // Exclude labels configuration
  const [excludeLabels, setExcludeLabels] = useState<string[]>(() => {
    try {
      const raw = localStorage.getItem('excludeLabels');
      return raw ? JSON.parse(raw) : [];
    } catch { return []; }
  });
  const [showExcludePanel, setShowExcludePanel] = useState(false);
  const [newExcludeLabel, setNewExcludeLabel] = useState("");

  const saveExcludeLabels = (labels: string[]) => {
    setExcludeLabels(labels);
    try {
      localStorage.setItem("excludeLabels", JSON.stringify(labels));
    } catch { }
  };

  // Visualizer settings
    const [customZoomPadding, setCustomZoomPadding] = useState<number>(60); // padding around duplicates (px)
    const [hoveredBoxId, setHoveredBoxId] = useState<string | null>(null);

  // Pagination for duplicate groups list
  const [currentPage, setCurrentPage] = useState(1);
  const itemsPerPage = 8;

  // Cleanup feedback
  const [isCleaning, setIsCleaning] = useState(false);



  // Refs
  const fileInputRef = useRef<HTMLInputElement>(null);
  const transformComponentRef = useRef<any>(null);

  // Trigger file browser
  const handleUploadClick = () => {
    fileInputRef.current?.click();
  };


  // Reset all state when new file starts uploading
  const resetState = () => {
    setFile(null);
    setError(null);
    setSuccessMsg(null);
    setZipEntries(null);
    setXmlFilesInZip([]);
    setSelectedXmlPath('');
    setXmlContent('');
    setXmlFilename('');
    setDataset(null);
    setSelectedGroupId(null);
    setSearchTerm('');
    setSelectedLabels([]);
    setCurrentPage(1);

    // Revoke image URL to avoid memory leaks
    if (currentImageSrc) {
      URL.revokeObjectURL(currentImageSrc);
    }
    setCurrentImageSrc(null);

    // Revoke manual images
    setManualImages(prev => {
      Object.keys(prev).forEach(key => {
        const url = prev[key];
        if (url) {
          URL.revokeObjectURL(url);
        }
      });
      return {};
    });
  };

  // Handle file select
  const handleFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files.length > 0) {
      await processUploadedFiles(e.target.files);
    }
    // reset input so the same file/folder can be selected again
    e.target.value = '';
  };

  // Drag and drop handlers
  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(true);
  };

  const handleDragLeave = () => {
    setIsDragging(false);
  };

  const handleDrop = async (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(false);
    if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
      await processUploadedFiles(e.dataTransfer.files);
    }
  };

  // Main file processor
  const processUploadedFiles = async (files: FileList | File[]) => {
    resetState();
    const fileArray = Array.from(files);
    if (fileArray.length === 0) return;

    setIsLoading(true);

    try {
      // Handle single file upload
      const uploadedFile = fileArray[0];
      setFile(uploadedFile);
      
      const extension = uploadedFile.name.split('.').pop()?.toLowerCase();
      if (extension === 'zip') {
        let entries: Entry[] = [];
        try {
          const zipFileReader = new BlobReader(uploadedFile);
          const zipReader = new ZipReader(zipFileReader);
          entries = await zipReader.getEntries();
          setZipEntries(entries);
        } catch (zipErr: any) {
          console.error("Lỗi nạp tệp ZIP:", zipErr);
          const errorMsg = zipErr.message || String(zipErr);
          throw new Error(
            `Không thể đọc tệp ZIP (Lỗi: ${errorMsg}).\n` +
            `💡 Mẹo: Tệp tin ZIP có thể bị hỏng.`
          );
        }

        const xmlPaths: string[] = [];
        entries.forEach((entry) => {
          const relativePath = entry.filename;
          if (relativePath.toLowerCase().endsWith('.xml') && !relativePath.startsWith('__MACOSX/')) {
            xmlPaths.push(relativePath);
          }
        });

        if (xmlPaths.length === 0) {
          throw new Error('Không tìm thấy tệp tin XML nào trong tệp ZIP đã tải lên.');
        }

        setXmlFilesInZip(xmlPaths);
        const defaultXml = xmlPaths.find(p => p.toLowerCase().includes('annotation')) || xmlPaths[0];
        setSelectedXmlPath(defaultXml);

        const xmlEntry = entries.find(e => e.filename === defaultXml);
        if (xmlEntry && (xmlEntry as any).getData) {
          const content = await (xmlEntry as any).getData(new TextWriter());
          setXmlContent(content);
          setXmlFilename(defaultXml.split('/').pop() || defaultXml);
        } else {
           throw new Error('Không thể đọc file XML trong ZIP.');
        }

      } else if (extension === 'xml') {
        try {
          const content = await uploadedFile.text();
          setXmlContent(content);
          setXmlFilename(uploadedFile.name);
        } catch (err) {
          throw new Error('Không thể đọc tệp XML này.');
        }
      } else {
        throw new Error('Định dạng tệp không được hỗ trợ. Vui lòng tải lên tệp .XML hoặc .ZIP.');
      }
    } catch (err: any) {
      setError(err.message || 'Lỗi không xác định khi tải tệp lên.');
    } finally {
      setIsLoading(false);
    }
  };

  // Parse XML when content changes
  useEffect(() => {
    if (!xmlContent) return;

    try {
      const parsed = parseCVATXML(xmlContent, xmlFilename);
      setDataset(parsed);
      setSelectedLabels(parsed.labels); // Tự động chọn tất cả nhãn khi nạp
      setSelectedGroupId(null);
      setCurrentPage(1);
    } catch (err: any) {
      setError('Lỗi khi phân tích XML: ' + err.message);
    }
  }, [xmlContent, xmlFilename]);

  // Calculate duplicates dynamically based on dataset and settings
  const duplicateGroups = useMemo(() => {
    if (!dataset) return [];
    return detectDuplicates(dataset, settings);
  }, [dataset, settings]);

  // Reset page when duplicates change or search filters update
  useEffect(() => {
    setCurrentPage(1);
    setSelectedGroupId(null);
  }, [searchTerm, selectedLabels, settings, frameRangeStart, frameRangeEnd]);

  // Extract statistics
  const stats = useMemo(() => {
    if (!dataset) {
      return {
        totalFrames: 0, totalBoxes: 0, totalDuplicates: 0, affectedFramesCount: 0, duplicatePercent: 0,
        firstBoxId: "—", lastBoxId: "—", labelBreakdown: [] as { label: string, count: number }[],
        totalValidBoxes: 0, excludeCount: 0, framesWithSkipCount: 0, finalCount: 0,
        frameRange: { min: 0, max: 0 }
      };
    }

    const ids = dataset.frames.map(f => parseInt(f.id, 10)).filter(id => !isNaN(id));
    const minDatasetFrame = ids.length ? Math.min(...ids) : 0;
    const maxDatasetFrame = ids.length ? Math.max(...ids) : 0;

    const sVal = frameRangeStart ? parseInt(frameRangeStart, 10) : minDatasetFrame;
    const eVal = frameRangeEnd ? parseInt(frameRangeEnd, 10) : maxDatasetFrame;
    const clampedStart = Math.max(minDatasetFrame, Math.min(sVal, maxDatasetFrame));
    const clampedEnd = Math.max(clampedStart, Math.min(eVal, maxDatasetFrame));

    const filteredFrames = dataset.frames.filter(img => {
      const id = parseInt(img.id, 10);
      return !isNaN(id) && id >= clampedStart && id <= clampedEnd;
    });

    const totalFrames = filteredFrames.length;
    let totalBoxes = 0;

    let minBoxId = Infinity;
    let maxBoxId = -Infinity;
    const labelCounts: Record<string, number> = {};

    let excludeCount = 0;
    let framesWithSkipCount = 0;
    const excludeSet = new Set(excludeLabels.map(x => x.toLowerCase()));

    filteredFrames.forEach(f => {
      totalBoxes += f.boxes.length;

      let frameHasSkip = f.boxes.length === 0; // frameNoBox
      let framePass = false;
      let frameSkipLabelCount = 0;
      let frameExtraExclude = 0;
      let exclBoxes = 0;

      f.boxes.forEach(b => {
        const id = parseInt(b.id, 10);
        if (!isNaN(id)) {
          if (id < minBoxId) minBoxId = id;
          if (id > maxBoxId) maxBoxId = id;
        }
        labelCounts[b.label] = (labelCounts[b.label] || 0) + 1;

        const lbl = b.label.toLowerCase();
        if (lbl === '_excl_area') exclBoxes++;
        if (excludeSet.has(lbl)) frameExtraExclude++;
        if (lbl.includes('skip')) frameSkipLabelCount++;

        const hasPassAttr = b.attributes.some(a => a.name.toLowerCase() === 'pass' && ['true', '1', 'yes', 'y', 'on'].includes(a.value.trim().toLowerCase()));
        if (hasPassAttr) framePass = true;
      });

      excludeCount += exclBoxes + frameExtraExclude;
      if (framePass) {
        excludeCount += f.boxes.length; // whole frame skipped
        frameHasSkip = true;
      } else {
        excludeCount += frameSkipLabelCount;
      }

      if (frameHasSkip || frameSkipLabelCount > 0) {
        framesWithSkipCount++;
      }
    });

    const firstBoxId = minBoxId === Infinity ? "—" : String(minBoxId);
    const lastBoxId = maxBoxId === -Infinity ? "—" : String(maxBoxId);

    const labelBreakdown = Object.entries(labelCounts)
      .map(([label, count]) => ({ label, count }))
      .sort((a, b) => b.count - a.count);

    // Count duplicate boxes (total boxes in groups minus 1 per group, which is kept)
    // Only count duplicates for filtered frames
    const relevantDuplicates = duplicateGroups.filter(g => {
      const frameId = parseInt(g.frameId, 10);
      return !isNaN(frameId) && frameId >= clampedStart && frameId <= clampedEnd;
    });

    const totalDuplicates = relevantDuplicates.reduce((sum, g) => sum + (g.boxes.length - 1), 0);
    const affectedFramesCount = new Set(relevantDuplicates.map(g => g.frameId)).size;
    const duplicatePercent = totalBoxes > 0 ? (totalDuplicates / totalBoxes) * 100 : 0;
    const finalCount = Math.max(0, totalBoxes - excludeCount);

    return {
      totalFrames,
      totalBoxes,
      totalDuplicates,
      affectedFramesCount,
      duplicatePercent: Math.round(duplicatePercent * 100) / 100,
      firstBoxId,
      lastBoxId,
      labelBreakdown,
      excludeCount,
      framesWithSkipCount,
      finalCount,
      totalValidBoxes: Math.max(0, totalBoxes - totalDuplicates),
      frameRange: { min: minDatasetFrame, max: maxDatasetFrame }
    };
  }, [dataset, duplicateGroups, frameRangeStart, frameRangeEnd, excludeLabels]);

  // Tính phạm vi frame (min/max) từ dataset
  const frameRange = useMemo(() => {
    if (!dataset || dataset.frames.length === 0) return { min: 0, max: 0 };
    const ids = dataset.frames.map(f => parseInt(f.id, 10)).filter(n => !isNaN(n));
    return { min: Math.min(...ids), max: Math.max(...ids) };
  }, [dataset]);

  // Filter duplicate groups based on search term and labels
  const baseFilteredGroups = useMemo(() => {
    return duplicateGroups.filter(group => {
      // Search matches frame name/id
      const matchesSearch = group.frameName.toLowerCase().includes(searchTerm.toLowerCase()) ||
        group.frameId.toString().includes(searchTerm);

      // Frame range filter
      const frameNum = parseInt(group.frameId, 10);
      const startOk = frameRangeStart === '' || isNaN(parseInt(frameRangeStart, 10)) || frameNum >= parseInt(frameRangeStart, 10);
      const endOk = frameRangeEnd === '' || isNaN(parseInt(frameRangeEnd, 10)) || frameNum <= parseInt(frameRangeEnd, 10);

      return matchesSearch && startOk && endOk;
    });
  }, [duplicateGroups, searchTerm, frameRangeStart, frameRangeEnd]);

  const filteredDuplicateGroups = useMemo(() => {
    if (selectedLabels.length === 0) return baseFilteredGroups;
    return baseFilteredGroups.filter(group => 
      group.boxes.some(box => selectedLabels.includes(box.label))
    );
  }, [baseFilteredGroups, selectedLabels]);

  // Paginated duplicate groups
  const paginatedGroups = useMemo(() => {
    const startIndex = (currentPage - 1) * itemsPerPage;
    return filteredDuplicateGroups.slice(startIndex, startIndex + itemsPerPage);
  }, [filteredDuplicateGroups, currentPage]);

  const totalPages = Math.ceil(filteredDuplicateGroups.length / itemsPerPage);

  // Selected group data
  const selectedGroup = useMemo(() => {
    if (!selectedGroupId) return null;
    return duplicateGroups.find(g => g.id === selectedGroupId) || null;
  }, [selectedGroupId, duplicateGroups]);

  // Frame details for selected group
  const selectedFrameData = useMemo(() => {
    if (!selectedGroup || !dataset) return null;
    return dataset.frames.find(f => f.id === selectedGroup.frameId) || null;
  }, [selectedGroup, dataset]);

  // Find image helper in zip
  const findImageInZip = (entries: Entry[], frameName: string): string | null => {
    const targetBaseName = frameName.split('/').pop()?.toLowerCase();
    if (!targetBaseName) return null;

    let matchedPath: string | null = null;
    entries.forEach((entry) => {
      const relativePath = entry.filename;
      if (entry.directory) return;

      const ext = relativePath.split('.').pop()?.toLowerCase();
      if (ext && ['png', 'jpg', 'jpeg', 'webp', 'bmp', 'gif', 'tiff'].includes(ext)) {
        const currentBaseName = relativePath.split('/').pop()?.toLowerCase();
        if (currentBaseName === targetBaseName || relativePath.toLowerCase() === frameName.toLowerCase()) {
          matchedPath = relativePath;
        }
      }
    });
    return matchedPath;
  };

  // Effect to load frame image from ZIP or manual map when active frame changes
  useEffect(() => {
    let active = true;
    let localUrl: string | null = null;

    const loadFrameImage = async () => {
      if (!selectedFrameData) {
        setCurrentImageSrc(null);
        return;
      }

      // 1. Check if we have a manual image uploaded for this frame
      if (manualImages[selectedFrameData.name]) {
        setCurrentImageSrc(manualImages[selectedFrameData.name]);
        setImageLoading(false);
        return;
      }

      // 2. Otherwise try loading from ZIP
      if (!zipEntries) {
        setCurrentImageSrc(null);
        return;
      }

      setImageLoading(true);
      try {
        const imgPath = findImageInZip(zipEntries, selectedFrameData.name);
        if (imgPath) {
          const entry = zipEntries.find(e => e.filename === imgPath);
          if (entry && (entry as any).getData) {
            const blob = await (entry as any).getData(new BlobWriter());
            if (active) {
              localUrl = URL.createObjectURL(blob);
              setCurrentImageSrc(localUrl);
            }
          } else {
            if (active) setCurrentImageSrc(null);
          }
        } else {
          if (active) setCurrentImageSrc(null);
        }
      } catch (err) {
        console.error("Lỗi khi đọc file ảnh từ tệp ZIP:", err);
        if (active) setCurrentImageSrc(null);
      } finally {
        if (active) setImageLoading(false);
      }
    };

    loadFrameImage();

    return () => {
      active = false;
      if (localUrl) {
        URL.revokeObjectURL(localUrl);
      }
    };
  }, [selectedFrameData, zipEntries, manualImages]);

  // Toggle label filter
  const handleLabelToggle = (label: string) => {
    if (selectedLabels.includes(label)) {
      if (selectedLabels.length > 1) {
        setSelectedLabels(selectedLabels.filter(l => l !== label));
      } else {
        // Must select at least one
        setSuccessMsg(null);
        setError('Bạn phải chọn ít nhất một nhãn để lọc.');
      }
    } else {
      setSelectedLabels([...selectedLabels, label]);
      setError(null);
    }
  };

  // Quick select all / none labels
  const handleSelectAllLabels = () => {
    if (!dataset) return;
    setSelectedLabels(dataset.labels);
  };

  // Download cleaned XML
  const handleDownloadCleanedXML = () => {
    if (!dataset || !xmlContent) return;

    try {
      setIsCleaning(true);
      const cleanedXml = removeDuplicatesFromXML(xmlContent, dataset, duplicateGroups);

      const blob = new Blob([cleanedXml], { type: 'text/xml;charset=utf-8;' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;

      // Output filename suffix with _cleaned
      const origName = xmlFilename;
      const baseName = origName.substring(0, origName.lastIndexOf('.')) || origName;
      link.setAttribute('download', `${baseName}_cleaned.xml`);
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);

      setSuccessMsg(`Đã tạo và tải xuống file XML đã làm sạch (đã xóa ${stats.totalDuplicates} box trùng lặp)!`);
    } catch (err: any) {
      setError('Lỗi khi tạo file XML đã làm sạch: ' + err.message);
    } finally {
      setIsCleaning(false);
    }
  };

  // Handle changing selected XML from ZIP
  const handleXmlPathChange = async (path: string) => {
    if (!zipEntries) return;
    try {
      setIsLoading(true);
      setSelectedXmlPath(path);
      
      const xmlEntry = zipEntries.find(e => e.filename === path);
      if (xmlEntry && (xmlEntry as any).getData) {
        const content = await (xmlEntry as any).getData(new TextWriter());
        setXmlContent(content);
        setXmlFilename(path.split('/').pop() || path);
        setSelectedGroupId(null);
        setCurrentPage(1);
      } else {
        throw new Error('Không thể đọc file XML trong ZIP.');
      }
    } catch (err: any) {
      setError(err.message || 'Lỗi khi tải file XML.');
    } finally {
      setIsLoading(false);
    }
  };

  // Download CSV report
  const handleDownloadCSVReport = () => {
    if (duplicateGroups.length === 0) return;

    try {
      const csvContent = generateCSVReport(duplicateGroups);
      const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;

      const origName = xmlFilename;
      const baseName = origName.substring(0, origName.lastIndexOf('.')) || origName;
      link.setAttribute('download', `${baseName}_duplicate_report.csv`);
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);

      setSuccessMsg('Đã tạo và tải xuống báo cáo trùng lặp dạng CSV thành công!');
    } catch (err: any) {
      setError('Lỗi khi xuất báo cáo CSV: ' + err.message);
    }
  };

  // Auto-scroll logic helper for selected frame bounding box
  const getZoomViewBox = () => {
    if (!selectedGroup || !selectedFrameData) return '0 0 1920 1080';

    const { width: frameWidth, height: frameHeight } = selectedFrameData;
    const { boxes } = selectedGroup;

    if (boxes.length === 0) return `0 0 ${frameWidth} ${frameHeight}`;

    // Get min/max bounds of the boxes in the group
    const minX = Math.min(...boxes.map(b => b.xtl));
    const minY = Math.min(...boxes.map(b => b.ytl));
    const maxX = Math.max(...boxes.map(b => b.xbr));
    const maxY = Math.max(...boxes.map(b => b.ybr));

    const w = maxX - minX;
    const h = maxY - minY;

    // Add visual breathing room (padding) around the duplicates, using user-defined padding
    const padding = customZoomPadding;

    const zoomX = Math.max(0, minX - padding);
    const zoomY = Math.max(0, minY - padding);
    const zoomW = Math.min(frameWidth - zoomX, w + padding * 2);
    const zoomH = Math.min(frameHeight - zoomY, h + padding * 2);

    return `${zoomX} ${zoomY} ${zoomW} ${zoomH}`;
  };

  return (
    <div className="dark-theme min-h-screen bg-slate-50 text-slate-800 font-sans flex flex-col antialiased">
      {/* Navigation Header */}
      <header className="bg-white border-b border-slate-200 sticky top-0 z-50 shadow-xs">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-4 flex items-center justify-between">
          <div className="flex items-center space-x-3">
            <div className="relative w-11 h-11 rounded-2xl bg-gradient-to-br from-cyan-400 via-blue-500 to-violet-600 shadow-lg shadow-cyan-500/25 ring-1 ring-white/20 overflow-hidden">
              <div className="absolute inset-0 bg-[radial-gradient(circle_at_30%_20%,rgba(255,255,255,0.75),transparent_34%)]" />
              <svg viewBox="0 0 44 44" className="relative w-full h-full drop-shadow-[0_8px_14px_rgba(0,0,0,0.35)]" aria-hidden="true">
                <path d="M22 8 35 14.5 22 21 9 14.5 22 8Z" fill="#f8fafc" opacity="0.96" />
                <path d="M9 14.5 22 21v14L9 28.5v-14Z" fill="#67e8f9" />
                <path d="M35 14.5 22 21v14l13-6.5v-14Z" fill="#a78bfa" />
                <path d="M14 14.5 22 10.5l8 4-8 4-8-4Z" fill="#0f172a" opacity="0.9" />
                <path d="M13 24.5 22 29l9-4.5" fill="none" stroke="#f8fafc" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" opacity="0.95" />
                <path d="M13 19.5 22 24l9-4.5" fill="none" stroke="#f8fafc" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" opacity="0.7" />
              </svg>
            </div>
            <div>
              <h1 className="text-xl font-bold tracking-tight text-slate-900">CVAT Box Counter & Duplicate Inspector</h1>
            </div>
          </div>
          <div className="flex items-center space-x-3">
            <h2 className="text-xl font-bold tracking-tight">
              <span className="font-sans font-medium text-slate-300">Build With Google AI Studio</span>
            </h2>
            <span className="w-2.5 h-2.5 rounded-full bg-emerald-500 animate-pulse"></span>
          </div>
        </div>
      </header>

      {/* Main Container */}
      <main className="flex-1 max-w-7xl w-full mx-auto p-4 sm:p-6 lg:p-8 space-y-6">

        {/* Upload Zone & Guide */}
        {!dataset && (
          <motion.div
            initial={{ opacity: 0, y: 15 }}
            animate={{ opacity: 1, y: 0 }}
            className="grid grid-cols-1 lg:grid-cols-12 gap-6"
          >
            {/* Left side: Upload card */}
            <div className="lg:col-span-7 bg-white rounded-3xl border border-slate-200 shadow-sm p-8 flex flex-col justify-between min-h-[420px]">
              <div>
                <h3 className="text-lg font-bold text-slate-900 mb-2">Tải tệp dữ liệu lên</h3>
                <p className="text-sm text-slate-500 mb-6">
                  Chọn hoặc kéo thả file XML/ZIP xuất từ CVAT để thống kê số box, lọc theo Frame Range, tính Exclude Labels, Frame Skip và kiểm tra Duplicate Boxes.
                </p>
              </div>

              {/* Drop area */}
              <div
                id="upload-dropzone"
                onClick={handleUploadClick}
                onDragOver={handleDragOver}
                onDragLeave={handleDragLeave}
                onDrop={handleDrop}
                className={`relative border-2 border-dashed rounded-2xl p-10 flex flex-col items-center justify-center cursor-pointer transition-all duration-200 group ${isDragging
                  ? 'border-red-500 bg-red-50/40 scale-[0.99]'
                  : 'border-slate-300 hover:border-red-400 hover:bg-slate-50/50'
                  }`}
              >
                <input
                  type="file"
                  ref={fileInputRef}
                  onChange={handleFileChange}
                  accept=".xml,.zip"
                  className="hidden"
                />
                


                <div className="w-16 h-16 bg-red-50 rounded-2xl flex items-center justify-center text-red-500 mb-4 group-hover:scale-110 transition-transform duration-200">
                  <FileUp className="w-8 h-8" />
                </div>

                <span className="text-base font-semibold text-slate-800 mb-1 group-hover:text-red-500">
                  Kéo thả tệp XML hoặc ZIP vào đây
                </span>
                <span className="text-xs text-slate-400">
                  Hoặc click để chọn từ thiết bị (Hỗ trợ XML đơn hoặc ZIP gói đầy đủ)
                </span>

                <div className="mt-6 flex space-x-3 text-xs text-slate-400 items-center">
                  <span className="flex items-center"><FileCode className="w-3.5 h-3.5 mr-1" /> CVAT XML</span>
                  <span className="border-r border-slate-200 h-3"></span>
                  <span className="flex items-center"><FileArchive className="w-3.5 h-3.5 mr-1" /> ZIP File</span>
                </div>
                

              </div>

              <div className="mt-6 text-xs text-slate-400 flex items-start space-x-2 bg-slate-50 p-3.5 rounded-xl border border-slate-150">
                <Info className="w-4 h-4 text-slate-500 shrink-0 mt-0.5" />
                <span>
                  <strong>Bảo mật:</strong> Tệp tin của bạn được phân tích cục bộ hoàn toàn trên trình duyệt của bạn bằng JavaScript. Không có dữ liệu hình ảnh hay chú thích nào được tải lên máy chủ ngoài.
                </span>
              </div>
            </div>

            {/* Right side: Instructions and info */}
            <div className="lg:col-span-5 bg-slate-900 text-white rounded-3xl p-8 flex flex-col justify-between shadow-xl shadow-slate-200 relative overflow-hidden">
              <div className="absolute top-0 right-0 transform translate-x-20 -translate-y-20 w-80 h-80 bg-red-500/10 rounded-full blur-3xl"></div>

              <div>
                <div className="inline-flex items-center space-x-1.5 bg-red-500/20 text-red-400 border border-red-500/30 px-3 py-1 rounded-full text-xs font-semibold mb-6">
                  <Sparkles className="w-3.5 h-3.5" />
                  <span>Box Audit Toolkit</span>
                </div>

                <h3 className="text-xl font-bold tracking-tight mb-4">Tính năng chính</h3>

                <ul className="space-y-4 text-sm text-slate-300">
                  <li className="flex items-start space-x-3">
                    <span className="flex items-center justify-center w-5 h-5 rounded-full bg-slate-800 text-xs font-bold text-red-400 shrink-0 mt-0.5">1</span>
                    <span><strong>Parse CVAT XML/ZIP:</strong> Đọc annotations từ định dạng Images hoặc Tracks.</span>
                  </li>
                  <li className="flex items-start space-x-3">
                    <span className="flex items-center justify-center w-5 h-5 rounded-full bg-slate-800 text-xs font-bold text-red-400 shrink-0 mt-0.5">2</span>
                    <span><strong>Box Counting:</strong> Tính số lượng Bounding Box theo Frame Range, Exclude Labels, Frame Skip và Passed frame.</span>
                  </li>
                  <li className="flex items-start space-x-3">
                    <span className="flex items-center justify-center w-5 h-5 rounded-full bg-slate-800 text-xs font-bold text-red-400 shrink-0 mt-0.5">3</span>
                    <span><strong>Duplicate Inspector:</strong> Quét Duplicate Boxes theo Pixel Match hoặc IoU, có tuỳ chọn chỉ tính trùng khi cùng Label.</span>
                  </li>
                  <li className="flex items-start space-x-3">
                    <span className="flex items-center justify-center w-5 h-5 rounded-full bg-slate-800 text-xs font-bold text-red-400 shrink-0 mt-0.5">4</span>
                    <span><strong>Visual Debug:</strong> Hiển thị ảnh/SVG đầy đủ, zoom vào vùng lỗi và lọc nhanh theo Frame, Label để kiểm tra annotation trực quan.</span>
                  </li>
                </ul>
              </div>

              <div className="mt-8 pt-6 border-t border-slate-800 text-xs text-slate-400 flex justify-between items-center">
                <span>Version v1.0</span>
                <span>CVAT XML / ZIP Support</span>
              </div>
            </div>
          </motion.div>
        )}

        {/* Global loading state */}
        {isLoading && (
          <div className="bg-white rounded-3xl border border-slate-200 p-12 flex flex-col items-center justify-center shadow-xs">
            <RefreshCw className="w-10 h-10 text-red-500 animate-spin mb-4" />
            <h4 className="text-lg font-semibold text-slate-800">Đang đọc và xử lý tệp dữ liệu...</h4>
            <p className="text-sm text-slate-500 mt-1">Quá trình này có thể mất vài giây tùy thuộc vào dung lượng file.</p>
          </div>
        )}

        {/* Global Error message */}
        {error && (
          <motion.div
            initial={{ opacity: 0, scale: 0.98 }}
            animate={{ opacity: 1, scale: 1 }}
            className="bg-rose-50 border border-rose-200 rounded-2xl p-4 flex items-start space-x-3 text-rose-800"
          >
            <AlertTriangle className="w-5 h-5 shrink-0 mt-0.5 text-rose-600" />
            <div className="flex-1">
              <h5 className="font-bold text-rose-900">Đã xảy ra lỗi</h5>
              <p className="text-sm mt-0.5">{error}</p>
            </div>
            <button onClick={() => setError(null)} className="text-rose-500 hover:text-rose-700 transition-colors">
              <X className="w-5 h-5" />
            </button>
          </motion.div>
        )}

        {/* Global Success message */}
        {successMsg && (
          <motion.div
            initial={{ opacity: 0, scale: 0.98 }}
            animate={{ opacity: 1, scale: 1 }}
            className="bg-emerald-50 border border-emerald-200 rounded-2xl p-4 flex items-start space-x-3 text-emerald-800"
          >
            <CheckCircle className="w-5 h-5 shrink-0 mt-0.5 text-emerald-600" />
            <div className="flex-1 text-sm font-medium">{successMsg}</div>
            <button onClick={() => setSuccessMsg(null)} className="text-emerald-500 hover:text-emerald-700 transition-colors">
              <X className="w-5 h-5" />
            </button>
          </motion.div>
        )}

        {/* Main Application Interface */}
        {dataset && (
          <div className="space-y-6">

            {/* File Info Bar & ZIP XML Selector */}
            <div className="bg-white rounded-2xl border border-slate-200 p-4 sm:p-5 shadow-xs flex flex-col md:flex-row md:items-center justify-between gap-4">
              <div className="flex items-center space-x-3.5">
                <div className="p-3 bg-red-50 rounded-xl text-red-500">
                  {zipEntries ? <FileArchive className="w-6 h-6" /> : <FileCode className="w-6 h-6" />}
                </div>
                <div>
                  <div className="flex items-center space-x-2">
                    <span className="font-bold text-slate-900 break-all">{file?.name}</span>
                    <span className="text-xs bg-slate-100 text-slate-600 border border-slate-200 px-2 py-0.5 rounded-md font-medium shrink-0">
                      {(file ? file.size / 1024 / 1024 : 0).toFixed(2)} MB
                    </span>
                  </div>
                  <p className="text-xs text-slate-500 mt-0.5">
                    Kiểu CVAT: <strong className="text-slate-700">{dataset.type === 'images' ? 'Bộ sưu tập ảnh (Images)' : 'Chuỗi khung hình video (Tracks)'}</strong>
                    {dataset.taskName && <> | Tên nhiệm vụ: <strong className="text-slate-700">{dataset.taskName}</strong></>}
                  </p>
                </div>
              </div>

              <div className="flex items-center space-x-3 self-end md:self-auto">
                {/* XML file selector if inside ZIP */}
                {zipEntries && xmlFilesInZip.length > 1 && (
                  <div className="flex items-center space-x-2">
                    <span className="text-xs font-semibold text-slate-500 shrink-0">Chọn file XML:</span>
                    <select
                      value={selectedXmlPath}
                      onChange={(e) => handleXmlPathChange(e.target.value)}
                      className="bg-slate-50 border border-slate-200 rounded-lg px-2.5 py-1.5 text-xs font-medium text-slate-700 hover:border-slate-300 focus:outline-hidden focus:ring-2 focus:ring-red-500/20"
                    >
                      {xmlFilesInZip.map(path => (
                        <option key={path} value={path}>
                          {path.length > 40 ? '...' + path.slice(-37) : path}
                        </option>
                      ))}
                    </select>
                  </div>
                )}

                <button
                  onClick={resetState}
                  className="inline-flex items-center space-x-1.5 border border-slate-200 text-slate-600 hover:bg-slate-50 px-3 py-1.5 rounded-lg text-xs font-semibold transition-colors"
                >
                  <X className="w-3.5 h-3.5" />
                  <span>Đóng file</span>
                </button>
              </div>
            </div>

            {/* Global Config: Frame Range & Exclude */}
            <div className="bg-white rounded-2xl border border-slate-200 shadow-xs p-5 space-y-4">
              <div className="flex items-center space-x-2 text-slate-800 pb-3 border-b border-slate-100">
                <Settings className="w-5 h-5 text-slate-500" />
                <h3 className="font-bold">Cấu hình Kiểm tra & Loại trừ Box</h3>
              </div>

              <div className="grid grid-cols-1 xl:grid-cols-2 gap-6 items-start">
                {/* Frame Range Filter */}
                <div className="space-y-3">
                  <span className="text-xs font-bold text-slate-500 block uppercase tracking-wider">1. Lọc theo vùng Frame (Giới hạn: {frameRange.min} - {frameRange.max})</span>
                  <div className="flex items-center gap-3">
                    <div className="flex-1">
                      <input
                        type="number"
                        min={frameRange.min}
                        max={frameRange.max}
                        placeholder={`Start: ${frameRange.min}`}
                        value={frameRangeStart}
                        onChange={(e) => setFrameRangeStart(e.target.value)}
                        className="w-full bg-slate-50 border border-slate-200 rounded-xl px-4 py-2.5 text-sm font-bold text-slate-700 placeholder-slate-400 focus:outline-hidden focus:ring-2 focus:ring-red-500/20 focus:bg-white transition-all font-mono"
                      />
                    </div>
                    <span className="text-slate-400 font-black">→</span>
                    <div className="flex-1">
                      <input
                        type="number"
                        min={frameRange.min}
                        max={frameRange.max}
                        placeholder={`End: ${frameRange.max}`}
                        value={frameRangeEnd}
                        onChange={(e) => setFrameRangeEnd(e.target.value)}
                        className="w-full bg-slate-50 border border-slate-200 rounded-xl px-4 py-2.5 text-sm font-bold text-slate-700 placeholder-slate-400 focus:outline-hidden focus:ring-2 focus:ring-red-500/20 focus:bg-white transition-all font-mono"
                      />
                    </div>
                    {(frameRangeStart !== String(frameRange.min) || frameRangeEnd !== String(frameRange.max)) && (
                      <button
                        onClick={() => { setFrameRangeStart(String(frameRange.min)); setFrameRangeEnd(String(frameRange.max)); }}
                        className="p-2.5 rounded-xl border border-slate-200 text-slate-500 hover:text-red-500 hover:bg-red-50 transition-colors"
                        title="Xóa bộ lọc vùng frame"
                      >
                        <X className="w-4 h-4" />
                      </button>
                    )}
                  </div>
                </div>

                {/* Exclude Labels Config */}
                <div className="space-y-3">
                  <span className="text-xs font-bold text-slate-500 block uppercase tracking-wider">2. Các Nhãn loại trừ khỏi tổng số đếm</span>
                  <div className="bg-slate-50 border border-slate-200 rounded-xl p-2.5 flex flex-wrap gap-2 items-center min-h-[46px]">
                    {excludeLabels.map(lbl => (
                      <span key={lbl} className="inline-flex items-center space-x-1 bg-red-100 text-red-700 px-2.5 py-1 rounded-lg text-xs font-bold">
                        <span>{lbl}</span>
                        <button onClick={() => saveExcludeLabels(excludeLabels.filter(x => x !== lbl))} className="hover:text-red-900"><X className="w-3 h-3" /></button>
                      </span>
                    ))}
                    <div className="flex-1 flex min-w-[150px]">
                      <input
                        type="text"
                        placeholder="Nhập tên nhãn (Vd: _corrupt) và Enter..."
                        value={newExcludeLabel}
                        onChange={e => setNewExcludeLabel(e.target.value)}
                        onKeyDown={e => {
                          if (e.key === 'Enter' && newExcludeLabel.trim()) {
                            if (!excludeLabels.includes(newExcludeLabel.trim())) {
                              saveExcludeLabels([...excludeLabels, newExcludeLabel.trim()]);
                            }
                            setNewExcludeLabel('');
                          }
                        }}
                        className="w-full bg-transparent border-none text-sm font-medium focus:ring-0 p-0 text-slate-700 placeholder-slate-400"
                      />
                    </div>
                  </div>
                </div>
              </div>
            </div>

            {/* Quick Stats Grid */}
            <div className="grid grid-cols-2 lg:grid-cols-7 gap-4">

              <div className="bg-blue-50 rounded-2xl border border-blue-200 p-4 shadow-xs border-l-4 border-l-blue-500 flex flex-col justify-center">
                <span className="text-xs font-bold text-blue-700 block uppercase tracking-wider">First ID</span>
                <div className="mt-1">
                  <span className="text-2xl font-black text-blue-700 font-mono">{stats.firstBoxId}</span>
                </div>
              </div>

              <div className="bg-amber-50 rounded-2xl border border-amber-200 p-4 shadow-xs border-l-4 border-l-amber-500 flex flex-col justify-center">
                <span className="text-xs font-bold text-amber-700 block uppercase tracking-wider">Last ID</span>
                <div className="mt-1">
                  <span className="text-2xl font-black text-amber-700 font-mono">{stats.lastBoxId}</span>
                </div>
              </div>

              <div className="bg-white rounded-2xl border border-slate-200 p-3.5 shadow-xs border-l-4 border-l-red-500 flex flex-col justify-center">
                <span className="text-xs font-semibold text-slate-400 block uppercase tracking-wider">Box Bị Loại Trừ</span>
                <div className="flex items-baseline space-x-1.5 mt-1">
                  <span className="text-2xl font-black text-red-600">{stats.excludeCount}</span>
                  <span className="text-[10px] text-slate-400 font-medium whitespace-nowrap leading-none">(Exclude)</span>
                </div>
              </div>

              <div className="bg-white rounded-2xl border border-slate-200 p-4 shadow-xs flex flex-col justify-center">
                <span className="text-xs font-semibold text-slate-400 block uppercase tracking-wider">Box Trùng Lặp</span>
                <div className="flex items-baseline space-x-1.5 mt-1">
                  <span className="text-2xl font-black text-rose-500">{stats.totalDuplicates}</span>
                  <span className="text-[10px] text-slate-400 font-medium whitespace-nowrap leading-none">(Duplicate)</span>
                </div>
              </div>

              <div className="bg-emerald-50 rounded-2xl border border-emerald-200 p-4 shadow-xs border-l-4 border-l-emerald-500 flex flex-col justify-center">
                <span className="text-xs font-bold text-emerald-700 block uppercase tracking-wider">Tổng Số Box</span>
                <div className="flex items-baseline space-x-1.5 mt-1">
                  <span className="text-2xl font-black text-emerald-600">{stats.finalCount}</span>
                  <span className="text-[9px] text-emerald-600 font-bold whitespace-nowrap leading-none">(Total Box)</span>
                </div>
              </div>

              <div className="bg-white rounded-2xl border border-slate-200 p-4 shadow-xs flex flex-col justify-center">
                <span className="text-xs font-semibold text-slate-400 block uppercase tracking-wider">Tổng số Frame</span>
                <div className="flex items-baseline space-x-1.5 mt-1">
                  <span className="text-2xl font-black text-slate-900">{stats.totalFrames}</span>
                  <span className="text-[10px] text-slate-400 font-medium whitespace-nowrap leading-none">frames</span>
                </div>
              </div>

              <div className="bg-slate-900 rounded-2xl p-4 shadow-xs text-white flex flex-col justify-center">
                <span className="text-xs font-semibold text-slate-400 block uppercase tracking-wider">Frame Skip</span>
                <div className="flex items-baseline space-x-1.5 mt-1">
                  <span className="text-2xl font-black text-red-400">{stats.framesWithSkipCount}</span>
                  <span className="text-[9px] text-slate-400 font-medium whitespace-nowrap leading-none">(Passed Frame)</span>
                </div>
              </div>
            </div>

            {/* Config & Audit Tool Controls */}
            <div className="bg-white rounded-2xl border border-slate-200 shadow-xs p-5 space-y-4">
              <div className="flex items-center space-x-2 text-slate-800 pb-3 border-b border-slate-100">
                <Settings className="w-5 h-5 text-slate-500" />
                <h3 className="font-bold">Cấu hình quét trùng lặp</h3>
              </div>

              <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">

                {/* Match Mode Selector */}
                <div className="lg:col-span-4 space-y-3">
                  <span className="text-xs font-bold text-slate-500 block">TIÊU CHÍ SO KHỚP TỌA ĐỘ</span>
                  <div className="grid grid-cols-2 gap-2 bg-slate-100 p-1 rounded-xl border border-slate-200">
                    <button
                      onClick={() => setSettings({ ...settings, useIoU: true })}
                      className={`py-2 px-3 rounded-lg text-xs font-bold transition-all ${settings.useIoU
                        ? 'bg-white text-slate-950 shadow-xs'
                        : 'text-slate-500 hover:text-slate-800'
                        }`}
                    >
                      Chỉ số IoU (Mức chồng đè)
                    </button>
                    <button
                      onClick={() => setSettings({ ...settings, useIoU: false })}
                      className={`py-2 px-3 rounded-lg text-xs font-bold transition-all ${!settings.useIoU
                        ? 'bg-white text-slate-950 shadow-xs'
                        : 'text-slate-500 hover:text-slate-800'
                        }`}
                    >
                      Sai lệch pixel (Cạnh trùng)
                    </button>
                  </div>
                </div>

                {/* Sliders for tolerance */}
                <div className="lg:col-span-5 space-y-3">
                  {!settings.useIoU ? (
                    <div>
                      <div className="flex justify-between items-center mb-1.5">
                        <span className="text-xs font-bold text-slate-500">
                          MỨC SAI LỆCH TOẠ ĐỘ CHO PHÉP (PIXEL)
                        </span>
                        <span className="text-xs font-black text-red-600 bg-red-50 border border-red-200 px-2 py-0.5 rounded-md">
                          {settings.tolerancePx === 0 ? '0.0 px (Trùng khớp 100%)' : `${settings.tolerancePx.toFixed(1)} px`}
                        </span>
                      </div>
                      <div className="flex items-center space-x-3">
                        <span className="text-xs font-bold text-slate-400">Khớp Tuyệt Đối</span>
                        <input
                          type="range"
                          min="0"
                          max="10"
                          step="0.5"
                          value={settings.tolerancePx}
                          onChange={(e) => setSettings({ ...settings, tolerancePx: parseFloat(e.target.value) })}
                          className="flex-1 accent-red-500 h-1.5 bg-slate-100 rounded-lg cursor-pointer"
                        />
                        <span className="text-xs font-bold text-slate-400">Sai lệch 10px</span>
                      </div>
                    </div>
                  ) : (
                    <div>
                      <div className="flex justify-between items-center mb-1.5">
                        <span className="text-xs font-bold text-slate-500">
                          HỆ SỐ TRÙNG NHAU TỐI THIỂU (IoU)
                        </span>
                        <span className="text-xs font-black text-red-600 bg-red-50 border border-red-200 px-2 py-0.5 rounded-md">
                          {settings.overlapThreshold}% trùng nhau
                        </span>
                      </div>
                      <div className="flex items-center space-x-3">
                        <span className="text-xs font-bold text-slate-400">50% Overlap</span>
                        <input
                          type="range"
                          min="50"
                          max="100"
                          step="1"
                          value={settings.overlapThreshold}
                          onChange={(e) => setSettings({ ...settings, overlapThreshold: parseInt(e.target.value, 10) })}
                          className="flex-1 accent-red-500 h-1.5 bg-slate-100 rounded-lg cursor-pointer"
                        />
                        <span className="text-xs font-bold text-slate-400">100% </span>
                      </div>
                    </div>
                  )}
                </div>

                {/* Match Label Option */}
                <div className="lg:col-span-3 pt-4 lg:pt-1 space-y-2">
                  <span className="text-xs font-bold text-slate-500 block uppercase tracking-wider">Cài đặt nhãn</span>
                  <label className="flex items-center space-x-3 cursor-pointer group bg-slate-50 p-2.5 rounded-xl border border-slate-150 hover:bg-slate-100/50 transition-colors">
                    <input
                      type="checkbox"
                      checked={settings.matchLabelOnly}
                      onChange={(e) => setSettings({ ...settings, matchLabelOnly: e.target.checked })}
                      className="w-4.5 h-4.5 accent-red-500 cursor-pointer rounded-sm"
                    />
                    <div className="text-xs font-semibold text-slate-700">
                      Chỉ tính trùng khi cùng Nhãn (Label)
                      <span className="block font-normal text-slate-400 text-[10px] mt-0.5">
                        {settings.matchLabelOnly ? 'Cùng toạ độ & cùng nhãn' : 'Trùng toạ độ kể cả khác nhãn'}
                      </span>
                    </div>
                  </label>
                </div>

              </div>
            </div>

            {/* Layout Workspaces: Left List vs Right Inspector */}
            <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-stretch">

              {/* Left Column: Duplicate Groups Directory */}
              <div className="lg:col-span-12 bg-white rounded-3xl border border-slate-200 shadow-xs flex flex-col min-h-[600px]">

                {/* Search and Filters Header */}
                <div className="p-4 border-b border-slate-100 space-y-3 shrink-0">
                  <div className="flex items-center justify-between">
                    <h3 className="font-bold text-slate-900 flex items-center space-x-2">
                      <span>Danh sách trùng lặp ({filteredDuplicateGroups.length})</span>
                      {filteredDuplicateGroups.length !== duplicateGroups.length && (
                        <span className="text-xs bg-slate-100 text-slate-500 px-2 py-0.5 rounded-md font-normal">
                          Lọc từ {duplicateGroups.length}
                        </span>
                      )}
                    </h3>
                  </div>

                  {/* Search box */}
                  <div className="relative">
                    <Search className="w-4 h-4 text-slate-400 absolute left-3 top-1/2 -translate-y-1/2" />
                    <input
                      type="text"
                      placeholder="Tìm theo tên ảnh hoặc ID frame..."
                      value={searchTerm}
                      onChange={(e) => setSearchTerm(e.target.value)}
                      className="w-full bg-slate-50 border border-slate-200 rounded-xl pl-9 pr-4 py-2 text-xs text-slate-700 placeholder-slate-400 focus:outline-hidden focus:ring-2 focus:ring-red-500/20 focus:bg-white transition-all"
                    />
                  </div>

                  {/* Labels filter badges */}
                  <div className="space-y-1.5">
                    <div className="flex justify-between items-center text-[10px] font-bold text-slate-400 tracking-wider">
                      <span>LỌC THEO NHÃN (LABELS)</span>
                      <button onClick={handleSelectAllLabels} className="text-red-500 hover:text-red-600 transition-colors">
                        Chọn tất cả
                      </button>
                    </div>
                    <div className="flex flex-wrap gap-1 max-h-[72px] overflow-y-auto pr-1">
                      {dataset.labels.map(label => {
                        const isSelected = selectedLabels.includes(label);
                        const countInDuplicates = baseFilteredGroups.reduce((sum, g) => {
                          const matches = g.boxes.filter(b => b.label === label);
                          if (matches.length > 1) {
                            return sum + (matches.length - 1);
                          } else if (matches.length === 1 && g.boxes.length > 1 && !settings.matchLabelOnly) {
                            return sum + 1;
                          }
                          return sum;
                        }, 0);

                        if (countInDuplicates === 0) return null; // Only show labels that actually have duplicates

                        return (
                          <button
                            key={label}
                            onClick={() => handleLabelToggle(label)}
                            className={`px-2 py-1 rounded-lg text-[10px] font-semibold flex items-center space-x-1 border transition-all ${isSelected
                              ? 'bg-red-50 border-red-200 text-red-700'
                              : 'bg-slate-50 border-slate-200 text-slate-400 hover:bg-slate-100 hover:text-slate-600'
                              }`}
                          >
                            <span>{label}</span>
                            <span className={`rounded-full px-1 py-0.2 text-[8px] ${isSelected ? 'bg-red-200/55' : 'bg-slate-200 text-slate-500'}`}>
                              {countInDuplicates}
                            </span>
                          </button>
                        );
                      })}
                    </div>
                  </div>
                </div>

                {/* List Container */}
                <div className="flex-1 overflow-y-auto min-h-0 p-4 bg-slate-50/50"><div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
                  <AnimatePresence mode="popLayout">
                    {paginatedGroups.length > 0 ? (
                      paginatedGroups.map((group, index) => {
                        const isSelected = selectedGroupId === group.id;
                        const absoluteIndex = (currentPage - 1) * itemsPerPage + index + 1;

                        return (
                          <motion.div
                            key={group.id}
                            initial={{ opacity: 0, x: -5 }}
                            animate={{ opacity: 1, x: 0 }}
                            exit={{ opacity: 0, x: -5 }}
                            onClick={() => setSelectedGroupId(group.id)}
                            className={`p-4 flex flex-col justify-between cursor-pointer transition-all rounded-2xl border shadow-xs ${isSelected
                              ? 'bg-red-50/50 border-red-300 ring-2 ring-red-500/20'
                              : 'bg-white border-slate-200 hover:border-red-300 hover:shadow-md'
                              }`}
                          >
                            <div className="min-w-0 pr-3">
                              {/* Tiêu đề chính: Frame ID to rõ */}
                              <div className="flex items-center space-x-2">
                                <span className="text-[11px] font-bold text-slate-400 font-mono">
                                  #{absoluteIndex}
                                </span>
                                <span className="text-sm font-extrabold text-slate-900">
                                  Frame {group.frameId}
                                </span>
                                <span className="text-[10px] bg-red-100 text-red-700 font-bold px-1.5 py-0.5 rounded">
                                  {group.boxes.length} box trùng
                                </span>
                              </div>

                              {/* Tên ảnh phụ + IoU + nhãn */}
                              <div className="flex items-center space-x-2 mt-1 flex-wrap gap-y-1">
                                <span className="text-[11px] text-slate-500 truncate max-w-[200px]" title={group.frameName}>
                                  📁 {group.frameName}
                                </span>
                                <span className="text-[10px] text-slate-400 font-semibold">
                                  IoU: {group.overlapPercentage}%
                                </span>
                                <span className="text-[10px] bg-slate-100 text-slate-700 font-semibold px-1.5 py-0.5 rounded truncate max-w-[120px]">
                                  {Array.from(new Set(group.boxes.map(b => b.label))).join(', ')}
                                </span>
                              </div>

                              {/* Chi tiết toạ độ box */}
                              <div className="mt-1.5 space-y-0.5">
                                {group.boxes.some(b => b.trackId) && (
                                  <span className="text-[10px] font-mono text-purple-700 bg-purple-50 border border-purple-200 px-1.5 py-0.5 rounded inline-block mb-0.5">
                                    Tracks: {Array.from(new Set(group.boxes.filter(b => b.trackId).map(b => b.trackId))).join(', ')}
                                  </span>
                                )}
                                <div className="text-[10px] text-slate-500 font-mono leading-relaxed">
                                  {group.boxes.slice(0, 2).map((box, bIdx) => (
                                    <span key={box.id} className="block truncate">
                                      <span className="text-slate-600 font-bold mr-1">
                                        #{box.globalIndex}
                                      </span>
                                      {box.label}: [{box.xtl.toFixed(1)}, {box.ytl.toFixed(1)}, {box.xbr.toFixed(1)}, {box.ybr.toFixed(1)}]
                                    </span>
                                  ))}
                                  {group.boxes.length > 2 && (
                                    <span className="text-slate-400 font-semibold">...+{group.boxes.length - 2} box khác</span>
                                  )}
                                </div>
                              </div>
                            </div>

                            <ChevronRight className={`w-4 h-4 text-slate-400 shrink-0 transition-transform ${isSelected ? 'translate-x-1 text-red-500' : ''
                              }`} />
                          </motion.div>
                        );
                      })
                    ) : (
                      <div className="py-20 text-center flex flex-col items-center justify-center p-6 text-slate-400">
                        <FileCheck className="w-12 h-12 text-slate-300 mb-3" />
                        <h4 className="font-bold text-sm text-slate-700">Không tìm thấy trùng lặp nào</h4>
                        <p className="text-xs text-slate-400 max-w-xs mt-1">
                          {duplicateGroups.length > 0
                            ? 'Không có kết quả khớp với bộ lọc tìm kiếm và nhãn của bạn.'
                            : 'Không có lỗi trùng lặp nào được phát hiện trong file dữ liệu này!'
                          }
                        </p>
                      </div>
                    )}
                  </AnimatePresence>
                  </div>
                </div>

                {/* Pagination footer */}
                {totalPages > 1 && (
                  <div className="p-3 border-t border-slate-100 bg-slate-50 flex items-center justify-between shrink-0 rounded-b-3xl">
                    <button
                      onClick={() => setCurrentPage(prev => Math.max(1, prev - 1))}
                      disabled={currentPage === 1}
                      className="p-1.5 rounded-lg border border-slate-200 text-slate-600 bg-white hover:bg-slate-100 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
                    >
                      <ChevronLeft className="w-4 h-4" />
                    </button>

                    <span className="text-[11px] font-bold text-slate-500 font-mono">
                      Trang {currentPage} / {totalPages}
                    </span>

                    <button
                      onClick={() => setCurrentPage(prev => Math.min(totalPages, prev + 1))}
                      disabled={currentPage === totalPages}
                      className="p-1.5 rounded-lg border border-slate-200 text-slate-600 bg-white hover:bg-slate-100 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
                    >
                      <ChevronRight className="w-4 h-4" />
                    </button>
                  </div>
                )}
              </div>

            </div>

          </div>
        )}

      </main>
      {/* Modal Preview */}
      <AnimatePresence>
        {selectedGroupId && selectedGroup && (
          <div className="fixed inset-0 z-[100] flex items-center justify-center p-4 sm:p-6 lg:p-8 bg-slate-950/80 backdrop-blur-sm">
            <motion.div
              initial={{ opacity: 0, scale: 0.95, y: 10 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.95, y: 10 }}
              transition={{ duration: 0.2, ease: "easeOut" }}
              className="w-full h-full max-w-7xl flex flex-col relative bg-white rounded-3xl shadow-2xl overflow-hidden"
            >
              
              {/* Right Column: Visual Inspector & Comparison table */}
              <div className="flex flex-col flex-1 h-full max-h-full">

                {/* SVG Visual Canvas Box */}
                <div className="bg-white rounded-3xl border border-slate-200 shadow-xs flex flex-col flex-1 overflow-hidden">

                  {/* Canvas header controls */}
                  <div className="p-4 border-b border-slate-100 bg-slate-50/50 flex flex-wrap items-center justify-between gap-3 shrink-0">
                    <div className="flex items-center space-x-2">
                      <Eye className="w-4.5 h-4.5 text-slate-500" />
                      <h3 className="font-bold text-slate-900 text-xs sm:text-sm">
                        {selectedFrameData
                          ? `Preview: Frame ${selectedFrameData.id}`
                          : 'Preview'
                        }
                      </h3>
                    </div>

                    <div className="flex items-center space-x-3">
                      {selectedFrameData && (
                        <div className="flex items-center space-x-2">
                          {/* Zoom to duplicates toggle with dynamic padding slider */}
                          <div className="flex items-center space-x-1 bg-slate-100 p-1 rounded-lg border border-slate-200">
                            <button
                              onClick={() => {
                                if (transformComponentRef.current) {
                                  transformComponentRef.current.zoomToElement('duplicate-group-bounds', undefined, 800, 'easeOut');
                                }
                              }}
                              className="p-1 rounded-md text-xs font-bold transition-all flex items-center space-x-1 bg-white text-blue-700 shadow-2xs border border-slate-200 hover:bg-slate-50"
                              title="Di chuyển đến vị trí lỗi trên ảnh"
                            >
                              <Maximize2 className="w-3.5 h-3.5 text-blue-600" />
                              <span className="hidden sm:inline">Đến vị trí lỗi</span>
                            </button>
                            <div className="flex items-center space-x-1 pl-1.5 border-l border-slate-200">
                              <input
                                type="range"
                                min="10"
                                max="400"
                                step="5"
                                value={customZoomPadding}
                                onChange={(e) => setCustomZoomPadding(parseInt(e.target.value, 10))}
                                className="w-12 sm:w-16 h-1 bg-slate-200 rounded-lg appearance-none cursor-pointer accent-blue-500"
                                title="Kéo về trái để zoom siêu gần sát đối tượng nhỏ (giảm khoảng đệm)"
                              />
                              <span className="text-[9px] sm:text-[10px] font-mono font-semibold text-slate-600 w-11 text-right" title="Khoảng cách lề bao quanh đối tượng (px). Càng nhỏ thì zoom càng cận!">
                                ±{customZoomPadding}px
                              </span>
                            </div>
                          </div>
                        </div>
                      )}
                      <button 
                        onClick={() => setSelectedGroupId(null)}
                        className="p-1.5 bg-slate-100 hover:bg-slate-200 text-slate-600 rounded-full transition-colors"
                        title="Đóng Preview"
                      >
                        <X className="w-5 h-5" />
                      </button>
                    </div>
                  </div>

                  {/* SVG drawing viewport */}
                  <div className="flex-1 bg-slate-950 relative flex items-center justify-center p-4 min-h-[300px]">
                    {selectedFrameData && selectedGroup ? (
                      <div className="w-full h-full flex flex-col justify-between items-center absolute inset-0 p-4">

                        {/* Coordinate indicator */}
                        <div className="self-start flex flex-wrap items-center gap-x-2 gap-y-1 text-[10px] font-mono text-slate-400 bg-slate-900/80 px-2.5 py-1 rounded-md border border-slate-800 backdrop-blur-xs z-10">
                          <span>Kích thước: {selectedFrameData.width} × {selectedFrameData.height} px</span>
                          <span className="text-slate-600">|</span>
                          {currentImageSrc ? (
                            <span className="text-emerald-400 flex items-center gap-1 font-sans font-bold">
                              <span className="w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse"></span>
                              Đã tải ảnh thực tế từ ZIP
                            </span>
                          ) : zipEntries ? (
                            <span className="text-amber-400 font-sans font-medium">Không tìm thấy ảnh Frame {selectedFrameData.id} trong ZIP</span>
                          ) : (
                            <span className="text-slate-500 font-sans">Chỉ có file XML (Vẽ mô phỏng)</span>
                          )}
                        </div>

                        {/* Responsive SVG Container */}
                        <div className="flex-1 w-full flex items-center justify-center relative overflow-hidden">
                          {imageLoading && (
                            <div className="absolute inset-0 bg-slate-950/70 backdrop-blur-xs flex flex-col items-center justify-center z-20">
                              <Loader2 className="w-8 h-8 text-red-500 animate-spin mb-2" />
                              <p className="text-xs text-slate-300 font-bold font-sans">Đang giải nén ảnh...</p>
                              <p className="text-[10px] text-slate-500 mt-0.5 font-mono truncate max-w-xs">Frame {selectedFrameData.id}</p>
                            </div>
                          )}

                          <CustomZoomPanPinch 
        contentWidth={selectedFrameData.width} 
        contentHeight={selectedFrameData.height}
        onZoomToElementRef={transformComponentRef}
      >
      
                            {({ state }) => {
                              const currentScale = state.scale || 1;
                              const dynamicStrokeWidth = (1.5 / currentScale).toString();
                              const dynamicHighlightStrokeWidth = (2 / currentScale).toString();
                              const labelScale = Math.min(1, 1 / currentScale * 2);

                              // Find all duplicate groups for this frame
                              const frameDuplicateGroups = duplicateGroups.filter(g => g.frameId === selectedFrameData.id);
                              const frameDuplicateBoxIds = new Set(frameDuplicateGroups.flatMap(g => g.boxes.map(b => b.id)));
                              const allDuplicateBoxesInFrame = selectedFrameData.boxes.filter(b => frameDuplicateBoxIds.has(b.id));

                              return (
                            
                              <svg
                                viewBox={`0 0 ${selectedFrameData.width} ${selectedFrameData.height}`}
                                className="w-full h-full border border-slate-800 shadow-2xl"
                              >
                            {/* Background drawing (Image or solid dark color) */}
                            {currentImageSrc ? (
                              <image
                                href={currentImageSrc}
                                width={selectedFrameData.width}
                                height={selectedFrameData.height}
                                
                                preserveAspectRatio="none"
                              />
                            ) : (
                              <rect
                                width={selectedFrameData.width}
                                height={selectedFrameData.height}
                                fill="#090d16"
                              />
                            )}

                            {selectedGroup && selectedFrameData && selectedGroup.boxes.length > 0 && (
                              <foreignObject id="duplicate-group-bounds"
                                x={Math.max(0, Math.min(...selectedGroup.boxes.map(b => b.xtl)) - customZoomPadding)}
                                y={Math.max(0, Math.min(...selectedGroup.boxes.map(b => b.ytl)) - customZoomPadding)}
                                width={Math.max(0, Math.max(...selectedGroup.boxes.map(b => b.xbr)) - Math.min(...selectedGroup.boxes.map(b => b.xtl)) + customZoomPadding * 2)}
                                height={Math.max(0, Math.max(...selectedGroup.boxes.map(b => b.ybr)) - Math.min(...selectedGroup.boxes.map(b => b.ytl)) + customZoomPadding * 2)}
                                pointerEvents="none"
                              >
                                <div style={{ width: '100%', height: '100%' }}></div>
                              </foreignObject>
                            )}

                            {/* DRAW ALL OTHER BOXES on this frame (Non-duplicates) */}
                            {selectedFrameData.boxes
                              .filter(b => !frameDuplicateBoxIds.has(b.id))
                              .map(box => (
                                <g key={box.id} className="opacity-30">
                                  <rect
                                    x={box.xtl}
                                    y={box.ytl}
                                    width={box.xbr - box.xtl}
                                    height={box.ybr - box.ytl}
                                    fill="none"
                                    stroke={getLabelColor(box.label, dataset)}
                                    strokeWidth={dynamicStrokeWidth}
                                    vectorEffect="non-scaling-stroke"
                                    strokeDasharray="4,4"
                                  />
                                  
                                  <g style={{ transform: `scale(${labelScale})`, transformOrigin: `${box.xtl}px ${box.ytl}px` }}>
                                    <text
                                      x={box.xtl + 4}
                                      y={box.ytl + 12}
                                      fill="#94a3b8"
                                      fontSize="10"
                                      fontWeight="bold"
                                      fontFamily="monospace"
                                    >
                                      {box.label}
                                    </text>
                                  </g>
                                </g>
                              ))}

                            {/* DRAW ALL DUPLICATE BOX GROUPS in this frame (High contrast highlighted) */}
                            {allDuplicateBoxesInFrame.map((box, idx) => {
                              const isFirst = idx % 2 === 0;
                              const isHovered = hoveredBoxId === box.id;

                              // Highlight duplicate group only; app no longer marks keep/delete boxes.
                              const color = getLabelColor(box.label, dataset);
                              const strokeWidth = dynamicHighlightStrokeWidth;

                              return (
                                <g
                                  key={box.id}
                                  className="cursor-pointer transition-all"
                                  onMouseEnter={() => setHoveredBoxId(box.id)}
                                  onMouseLeave={() => setHoveredBoxId(null)}
                                >
                                  {/* Bounding box rect */}
                                  <rect
                                    x={box.xtl}
                                    y={box.ytl}
                                    width={box.xbr - box.xtl}
                                    height={box.ybr - box.ytl}
                                    fill={color + "1a"}
                                    stroke={color}
                                    strokeWidth={strokeWidth}
                                    vectorEffect="non-scaling-stroke"
                                    style={{ transition: 'stroke-width 0.15s ease' }}
                                  />

                                  {/* Small label badge at top-left of box */}
                                  <g style={{ transform: `scale(${labelScale})`, transformOrigin: `${box.xtl}px ${box.ytl}px` }}>
                                    <rect
                                      x={box.xtl}
                                      y={box.ytl - (isFirst ? 18 : 34)}
                                      width={Math.max((box.label.length * 7) + 30, 95)}
                                      height="16"
                                      fill={color}
                                      rx="2"
                                    />

                                    <text
                                      x={box.xtl + 5}
                                      y={box.ytl - (isFirst ? 6 : 22)}
                                      fill="#ffffff"
                                      fontSize="9.5"
                                      fontWeight="black"
                                      fontFamily="sans-serif"
                                    >
                                      #{box.globalIndex} {box.label}
                                    </text>
                                  </g>
                                </g>
                              );
                            })}
                          </svg>
                            
                              );
                            }}
                          
      </CustomZoomPanPinch>
                        </div>

                        {/* Visual label note & manual image uploader removed */}

                      </div>
                    ) : (
                      <div className="text-center p-10 flex flex-col items-center justify-center max-w-sm text-slate-500">
                        <Layers className="w-16 h-16 text-slate-800 mb-4 animate-pulse" />
                        <h4 className="font-bold text-sm text-slate-300">Chọn một tệp trùng lặp bên trái</h4>
                        <p className="text-xs text-slate-500 mt-2">
                          Nhấp chọn bất kỳ cặp trùng lặp nào trong danh mục bên trái để soi tọa độ cận cảnh trực quan.
                        </p>
                      </div>
                    )}
                  </div>
                </div>

                {/* Phần bảng chi tiết đã được xoá theo yêu cầu để nhường không gian cho Canvas SVG */}

              </div>
            </motion.div>
          </div>
        )}
      </AnimatePresence>

      {/* Modal Preview */}
      <AnimatePresence>
        {selectedGroupId && selectedGroup && selectedFrameData && (
          <div className="fixed inset-0 z-50 flex items-center justify-center p-4 sm:p-6 lg:p-8 bg-slate-950/80 backdrop-blur-sm">
            <motion.div
              initial={{ opacity: 0, scale: 0.95, y: 10 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.95, y: 10 }}
              transition={{ duration: 0.2, ease: "easeOut" }}
              className="w-full h-full max-w-7xl flex flex-col relative"
            >
              
            </motion.div>
          </div>
        )}
      </AnimatePresence>


      {/* App Footer */}
      <footer className="bg-white border-t border-slate-200 py-6 mt-12 text-center text-xs text-slate-400">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 space-y-2">
          <p>© 2026 CVAT Duplicate Bounding Box Auditor.</p>
          <p className="font-mono text-[10px] text-slate-300">Built using React 19 + TypeScript + zip.js + Tailwind CSS</p>
        </div>
      </footer>
    </div>
  );
}
