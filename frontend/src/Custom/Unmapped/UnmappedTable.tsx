import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import Label from 'Components/Label';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { kinds } from 'Helpers/Props';
import InteractiveImportModal from 'InteractiveImport/InteractiveImportModal';
import translate from 'Utilities/String/translate';

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
      ? (STATUS_NAMES[status] ?? String(status))
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

interface UnmappedActionButtonsProps {
  item: LocalWatchBufferItem;
  onManualImport: (path: string) => void;
  onChanged: () => void;
}

function UnmappedActionButtons({
  item,
  onManualImport,
  onChanged,
}: UnmappedActionButtonsProps) {
  const retryMutation = useApiMutation<LocalWatchBufferItem, void>({
    path: `/localwatchbuffer/${item.id}/retry`,
    method: 'POST',
    mutationOptions: {
      onSuccess: onChanged,
    },
  });

  const ignoreMutation = useApiMutation<LocalWatchBufferItem, void>({
    path: `/localwatchbuffer/${item.id}/ignore`,
    method: 'POST',
    mutationOptions: {
      onSuccess: onChanged,
    },
  });

  const isBusy = retryMutation.isPending || ignoreMutation.isPending;

  return (
    <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap' }}>
      <Button
        isDisabled={isBusy}
        onPress={() => onManualImport(item.path)}
      >
        {translate('ManualImport')}
      </Button>
      <Button
        kind={kinds.PRIMARY}
        isDisabled={isBusy}
        onPress={() => retryMutation.mutate()}
      >
        Retry
      </Button>
      <Button
        kind={kinds.DEFAULT}
        isDisabled={isBusy}
        onPress={() => ignoreMutation.mutate()}
      >
        Ignore
      </Button>
    </div>
  );
}

function UnmappedTable() {
  const queryClient = useQueryClient();
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [selectedFolder, setSelectedFolder] = useState<string | undefined>(
    undefined
  );

  const { data: files = [], isLoading, error } = useApiQuery<
    LocalWatchBufferItem[]
  >({
    path: '/localwatchbuffer',
  });

  const invalidateBuffer = useCallback(() => {
    queryClient.invalidateQueries({ queryKey: ['/localwatchbuffer'] });
  }, [queryClient]);

  const handleManualImport = useCallback((path: string) => {
    setSelectedFolder(path);
    setIsModalOpen(true);
  }, []);

  const handleModalClose = useCallback(() => {
    setIsModalOpen(false);
    invalidateBuffer();
  }, [invalidateBuffer]);

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
      <Alert kind={kinds.DANGER}>
        Unable to load watch folder queue.
      </Alert>
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
                    onManualImport={handleManualImport}
                    onChanged={invalidateBuffer}
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
          onModalClose={handleModalClose}
        />
      ) : null}
    </div>
  );
}

export default UnmappedTable;
