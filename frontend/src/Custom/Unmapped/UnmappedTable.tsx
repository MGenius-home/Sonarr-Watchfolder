import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import Label from 'Components/Label';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { kinds } from 'Helpers/Props';
import InteractiveImportModal from 'InteractiveImport/InteractiveImportModal';
import UnmappedActionButtons from './UnmappedActionButtons';

export interface LocalWatchBufferItem {
  id: number;
  path: string;
  status: string | number;
  lastSeen: string;
  size?: number | null;
  failures: number;
  reason?: string | null;
}

const STATUS_NAMES: Record<number, string> = {
  0: 'Pending',
  1: 'Unmapped',
  2: 'Imported',
  3: 'Ignored',
  4: 'Failed',
};

function formatStatus(status: string | number): {
  label: string;
  kind: 'warning' | 'danger' | 'success' | 'default' | 'info';
} {
  const normalized =
    typeof status === 'number'
      ? STATUS_NAMES[status] ?? String(status)
      : String(status);
  const lower = normalized.toLowerCase();

  if (lower === 'failed') {
    return { label: 'Failed', kind: kinds.DANGER };
  }

  if (lower === 'unmapped') {
    return { label: 'Unmapped', kind: kinds.WARNING };
  }

  if (lower === 'imported') {
    return { label: 'Imported', kind: kinds.SUCCESS };
  }

  if (lower === 'pending') {
    return { label: 'Pending', kind: kinds.INFO };
  }

  return {
    label: normalized.charAt(0).toUpperCase() + normalized.slice(1),
    kind: kinds.DEFAULT,
  };
}

function UnmappedTable() {
  const queryClient = useQueryClient();
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [selectedFolder, setSelectedFolder] = useState<string | undefined>(
    undefined
  );

  const {
    data: files = [],
    isLoading,
    error,
  } = useApiQuery<LocalWatchBufferItem[]>({
    path: '/localwatchbuffer',
  });

  const onBufferChanged = useCallback(() => {
    queryClient.invalidateQueries({ queryKey: ['/localwatchbuffer'] });
  }, [queryClient]);

  const onManualImport = useCallback((path: string) => {
    setSelectedFolder(path);
    setIsModalOpen(true);
  }, []);

  const onModalClose = useCallback(() => {
    setIsModalOpen(false);
    onBufferChanged();
  }, [onBufferChanged]);

  const columns = [
    { name: 'path', label: 'File Path', isVisible: true },
    { name: 'status', label: 'Status', isVisible: true },
    { name: 'reason', label: 'Reason', isVisible: true },
    { name: 'failures', label: 'Failures', isVisible: true },
    { name: 'actions', label: 'Actions', isVisible: true },
  ];

  if (isLoading) {
    return <LoadingIndicator />;
  }

  if (error) {
    return (
      <Alert kind={kinds.DANGER}>Unable to load watch folder queue.</Alert>
    );
  }

  if (!files.length) {
    return (
      <Alert kind={kinds.INFO}>
        No unmapped or failed files in the watch folder.
      </Alert>
    );
  }

  return (
    <div>
      <Table columns={columns}>
        <TableBody>
          {files.map((file) => {
            const statusInfo = formatStatus(file.status);

            return (
              <TableRow key={file.id}>
                <TableRowCell>{file.path}</TableRowCell>
                <TableRowCell>
                  <Label kind={statusInfo.kind}>{statusInfo.label}</Label>
                </TableRowCell>
                <TableRowCell>{file.reason || '-'}</TableRowCell>
                <TableRowCell>{file.failures}</TableRowCell>
                <TableRowCell>
                  <UnmappedActionButtons
                    item={file}
                    onManualImport={onManualImport}
                    onChanged={onBufferChanged}
                  />
                </TableRowCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
      {isModalOpen ? (
        <InteractiveImportModal
          isOpen={isModalOpen}
          folder={selectedFolder}
          onModalClose={onModalClose}
        />
      ) : null}
    </div>
  );
}

export default UnmappedTable;
