import React from 'react';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import translate from 'Utilities/String/translate';
import UnmappedTable from './UnmappedTable';

function UnmappedLayout() {
  return (
    <PageContent title={translate('Unmapped Local Files')}>
      <PageContentBody>
        <UnmappedTable />
      </PageContentBody>
    </PageContent>
  );
}

export default UnmappedLayout;
